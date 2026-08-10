using System.Collections.Immutable;
using Anthropic.Models.Messages;
using ClaudeResearchAgent.Agent;
using ClaudeResearchAgent.Models;
using ClaudeResearchAgent.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static ClaudeResearchAgent.Tests.TestSupport.AnthropicMessageFactory;

namespace ClaudeResearchAgent.Tests;

public class ResearchAgentTests
{
    private const string ValidFinalJson = """
        {
          "topic": "Hammerhead Sharks",
          "summary": "A family of sharks named for their distinctive head shape.",
          "sources": [],
          "toolsUsed": []
        }
        """;

    private static AgentOptions Options(int maxIterations = 8, int repairAttempts = 1) => new()
    {
        Model = "claude-sonnet-5",
        MaxIterations = maxIterations,
        OverallTimeoutSeconds = 30,
        ToolTimeoutSeconds = 10,
        MaximumToolResultCharacters = 12_000,
        MaximumSaveCharacters = 50_000,
        MaximumFormatRepairAttempts = repairAttempts,
        MaxTokens = 1024,
    };

    private static ResearchAgent BuildAgent(
        FakeMessageService messageService,
        AgentOptions? options = null,
        params StubAgentTool[] tools)
    {
        var registry = new ToolRegistry(tools, options ?? Options(), NullLogger<ToolRegistry>.Instance);
        var parser = new ResearchResponseParser(NullLogger<ResearchResponseParser>.Instance);
        return new ResearchAgent(messageService, registry, parser, options ?? Options(), NullLogger<ResearchAgent>.Instance);
    }

    [Fact]
    public async Task Returns_a_direct_final_answer_when_no_tool_is_needed()
    {
        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.EndTurn, Text(ValidFinalJson)));
        var agent = BuildAgent(messageService);

        var response = await agent.ResearchAsync("What are hammerhead sharks?", CancellationToken.None);

        Assert.Equal("Hammerhead Sharks", response.Topic);
        Assert.Single(messageService.Requests);
        Assert.Empty(response.ToolsUsed);
    }

    [Fact]
    public async Task Executes_one_tool_then_returns_the_final_answer()
    {
        const string toolUseId = "toolu_01";
        var wikipediaSource = new ResearchSource { Title = "Hammerhead shark", Url = "https://en.wikipedia.org/wiki/Hammerhead_shark" };
        var wikipedia = StubAgentTool.ReturningOk("wikipedia", """{"success":true}""", [wikipediaSource]);

        var finalJsonCitingRealSource = $$"""
            {
              "topic": "Hammerhead Sharks",
              "summary": "Summary.",
              "sources": [ { "title": "Hammerhead shark", "url": "{{wikipediaSource.Url}}" } ],
              "toolsUsed": ["wikipedia"]
            }
            """;

        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.ToolUse, ToolUse(toolUseId, "wikipedia", new { query = "hammerhead shark" })))
            .Enqueue(Build(StopReason.EndTurn, Text(finalJsonCitingRealSource)));

        var agent = BuildAgent(messageService, tools: [wikipedia]);

        var response = await agent.ResearchAsync("Tell me about hammerhead sharks", CancellationToken.None);

        Assert.Equal(1, wikipedia.CallCount);
        Assert.Equal(["wikipedia"], response.ToolsUsed);
        Assert.Single(response.Sources);
        Assert.Equal(wikipediaSource.Url, response.Sources[0].Url);

        // The tool_result sent back must reference the exact tool_use_id Claude issued.
        var toolResultsRequest = messageService.Requests[1];
        var toolResultMessage = Assert.Single(toolResultsRequest.Messages, m => m.Role == Role.User && m.Content.Value is ImmutableArray<ContentBlockParam>);
        var toolResultBlock = Assert.IsType<ImmutableArray<ContentBlockParam>>(toolResultMessage.Content.Value!)[0];
        Assert.True(toolResultBlock.TryPickToolResult(out var toolResult));
        Assert.Equal(toolUseId, toolResult!.ToolUseID);
    }

    [Fact]
    public async Task Strips_invented_sources_and_uses_recorded_tool_names_as_authoritative()
    {
        var realSource = new ResearchSource { Title = "Real", Url = "https://en.wikipedia.org/wiki/Real" };
        var wikipedia = StubAgentTool.ReturningOk("wikipedia", """{"success":true}""", [realSource]);

        // Claude cites one real source and one it invented, and claims a tool ("search") that
        // never actually ran.
        const string dishonestFinalJson = """
            {
              "topic": "T",
              "summary": "S",
              "sources": [
                { "title": "Real", "url": "https://en.wikipedia.org/wiki/Real" },
                { "title": "Invented", "url": "https://example.test/invented" }
              ],
              "toolsUsed": ["wikipedia", "search"]
            }
            """;

        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.ToolUse, ToolUse("id1", "wikipedia", new { query = "x" })))
            .Enqueue(Build(StopReason.EndTurn, Text(dishonestFinalJson)));

        var agent = BuildAgent(messageService, tools: [wikipedia]);

        var response = await agent.ResearchAsync("question", CancellationToken.None);

        Assert.Equal(["wikipedia"], response.ToolsUsed);
        Assert.Single(response.Sources);
        Assert.Equal(realSource.Url, response.Sources[0].Url);
    }

    [Fact]
    public async Task Executes_multiple_sequential_tool_calls_across_iterations()
    {
        var wikipedia = StubAgentTool.ReturningOk("wikipedia", """{"success":true}""");
        var search = StubAgentTool.ReturningOk("search", """{"success":true}""");

        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.ToolUse, ToolUse("id1", "wikipedia", new { query = "x" })))
            .Enqueue(Build(StopReason.ToolUse, ToolUse("id2", "search", new { query = "y" })))
            .Enqueue(Build(StopReason.EndTurn, Text(ValidFinalJson)));

        var agent = BuildAgent(messageService, tools: [wikipedia, search]);

        var response = await agent.ResearchAsync("question", CancellationToken.None);

        Assert.Equal(1, wikipedia.CallCount);
        Assert.Equal(1, search.CallCount);
        Assert.Equal(3, messageService.Requests.Count);
        Assert.Equal(2, response.ToolsUsed.Count);
    }

    [Fact]
    public async Task Executes_multiple_tool_requests_from_a_single_assistant_turn_with_correct_ids()
    {
        var wikipedia = StubAgentTool.ReturningOk("wikipedia", """{"from":"wikipedia"}""");
        var search = StubAgentTool.ReturningOk("search", """{"from":"search"}""");

        var messageService = new FakeMessageService()
            .Enqueue(Build(
                StopReason.ToolUse,
                ToolUse("id_wiki", "wikipedia", new { query = "a" }),
                ToolUse("id_search", "search", new { query = "b" })))
            .Enqueue(Build(StopReason.EndTurn, Text(ValidFinalJson)));

        var agent = BuildAgent(messageService, tools: [wikipedia, search]);

        await agent.ResearchAsync("question", CancellationToken.None);

        Assert.Equal(1, wikipedia.CallCount);
        Assert.Equal(1, search.CallCount);

        var toolResultsRequest = messageService.Requests[1];
        var toolResultMessage = toolResultsRequest.Messages.Last();
        var blocks = Assert.IsType<ImmutableArray<ContentBlockParam>>(toolResultMessage.Content.Value!);
        Assert.Equal(2, blocks.Length);

        Assert.True(blocks[0].TryPickToolResult(out var first));
        Assert.Equal("id_wiki", first!.ToolUseID);
        Assert.True(blocks[1].TryPickToolResult(out var second));
        Assert.Equal("id_search", second!.ToolUseID);
    }

    [Fact]
    public async Task Handles_an_unknown_tool_gracefully_and_keeps_going()
    {
        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.ToolUse, ToolUse("id1", "not_a_real_tool", new { })))
            .Enqueue(Build(StopReason.EndTurn, Text(ValidFinalJson)));

        var agent = BuildAgent(messageService);

        var response = await agent.ResearchAsync("question", CancellationToken.None);

        Assert.Equal("Hammerhead Sharks", response.Topic);
        Assert.Empty(response.ToolsUsed); // the unknown tool never "successfully executed"
    }

    [Fact]
    public async Task Stops_with_a_bounded_failure_at_the_max_iteration_limit()
    {
        var wikipedia = StubAgentTool.ReturningOk("wikipedia", """{"success":true}""");
        var options = Options(maxIterations: 2);

        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.ToolUse, ToolUse("id1", "wikipedia", new { query = "x" })))
            .Enqueue(Build(StopReason.ToolUse, ToolUse("id2", "wikipedia", new { query = "x" })));

        var agent = BuildAgent(messageService, options, wikipedia);

        var ex = await Assert.ThrowsAsync<AgentExecutionException>(
            () => agent.ResearchAsync("question", CancellationToken.None));

        Assert.Equal(AgentFailureReason.MaxIterationsExceeded, ex.Reason);
        Assert.Equal(2, messageService.Requests.Count);
    }

    [Fact]
    public async Task Propagates_cancellation_requested_before_any_call_completes()
    {
        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.EndTurn, Text(ValidFinalJson)));
        var agent = BuildAgent(messageService);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => agent.ResearchAsync("question", cts.Token));
    }

    [Fact]
    public async Task Handles_a_refusal_as_a_bounded_failure()
    {
        var messageService = new FakeMessageService()
            .Enqueue(BuildRefusal("This request violates usage policies."));
        var agent = BuildAgent(messageService);

        var ex = await Assert.ThrowsAsync<AgentExecutionException>(
            () => agent.ResearchAsync("question", CancellationToken.None));

        Assert.Equal(AgentFailureReason.Refusal, ex.Reason);
        Assert.Contains("usage policies", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finds_the_text_block_even_when_a_thinking_block_comes_first()
    {
        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.EndTurn, Thinking("internal reasoning nobody should see"), Text(ValidFinalJson)));
        var agent = BuildAgent(messageService);

        var response = await agent.ResearchAsync("question", CancellationToken.None);

        Assert.Equal("Hammerhead Sharks", response.Topic);
    }

    [Fact]
    public async Task Finds_the_text_block_even_when_a_redacted_thinking_block_comes_first()
    {
        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.EndTurn, RedactedThinking("opaque-data"), Text(ValidFinalJson)));
        var agent = BuildAgent(messageService);

        var response = await agent.ResearchAsync("question", CancellationToken.None);

        Assert.Equal("Hammerhead Sharks", response.Topic);
    }

    [Fact]
    public async Task Throws_when_the_response_has_neither_text_nor_a_tool_call()
    {
        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.EndTurn, Thinking("only thinking, nothing else")));
        var agent = BuildAgent(messageService);

        var ex = await Assert.ThrowsAsync<AgentExecutionException>(
            () => agent.ResearchAsync("question", CancellationToken.None));

        Assert.Equal(AgentFailureReason.NoUsableResponse, ex.Reason);
    }

    [Fact]
    public async Task Performs_at_most_the_configured_number_of_repair_attempts()
    {
        var options = Options(repairAttempts: 1);
        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.EndTurn, Text("this is not json")))
            .Enqueue(Build(StopReason.EndTurn, Text("still not json")));

        var agent = BuildAgent(messageService, options);

        var ex = await Assert.ThrowsAsync<AgentExecutionException>(
            () => agent.ResearchAsync("question", CancellationToken.None));

        Assert.Equal(AgentFailureReason.InvalidStructuredOutput, ex.Reason);
        Assert.Equal(2, messageService.Requests.Count); // initial attempt + exactly one repair attempt
    }

    [Fact]
    public async Task Recovers_after_one_repair_attempt_when_the_second_response_is_valid()
    {
        var options = Options(repairAttempts: 1);
        var messageService = new FakeMessageService()
            .Enqueue(Build(StopReason.EndTurn, Text("this is not json")))
            .Enqueue(Build(StopReason.EndTurn, Text(ValidFinalJson)));

        var agent = BuildAgent(messageService, options);

        var response = await agent.ResearchAsync("question", CancellationToken.None);

        Assert.Equal("Hammerhead Sharks", response.Topic);
        Assert.Equal(2, messageService.Requests.Count);
    }
}
