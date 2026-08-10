using System.Text.Json;
using ClaudeResearchAgent.Agent;
using ClaudeResearchAgent.Models;
using ClaudeResearchAgent.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClaudeResearchAgent.Tests;

public class ToolRegistryTests
{
    private static AgentOptions DefaultOptions(int toolTimeoutSeconds = 30, int maxObservationCharacters = 12_000) => new()
    {
        Model = "claude-sonnet-5",
        ToolTimeoutSeconds = toolTimeoutSeconds,
        MaximumToolResultCharacters = maxObservationCharacters,
    };

    [Fact]
    public void Resolves_registered_tools_by_name()
    {
        var toolA = StubAgentTool.ReturningOk("alpha", "ok");
        var toolB = StubAgentTool.ReturningOk("beta", "ok");
        var registry = new ToolRegistry([toolA, toolB], DefaultOptions(), NullLogger<ToolRegistry>.Instance);

        Assert.True(registry.TryGet("alpha", out var resolvedA));
        Assert.Same(toolA, resolvedA);
        Assert.True(registry.TryGet("beta", out var resolvedB));
        Assert.Same(toolB, resolvedB);
        Assert.Equal(2, registry.RegisteredTools.Count);
    }

    [Fact]
    public void Rejects_duplicate_tool_names()
    {
        var registry = new ToolRegistry(
            [StubAgentTool.ReturningOk("alpha", "ok")], DefaultOptions(), NullLogger<ToolRegistry>.Instance);

        var duplicate = StubAgentTool.ReturningOk("alpha", "also ok");

        Assert.Throws<InvalidOperationException>(() => registry.Register(duplicate));
    }

    [Fact]
    public async Task Returns_a_safe_failure_for_an_unknown_tool()
    {
        var registry = new ToolRegistry([], DefaultOptions(), NullLogger<ToolRegistry>.Instance);

        var result = await registry.ExecuteAsync("does_not_exist", default, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("unknown_tool", result.ErrorCategory);
        Assert.Contains("does_not_exist", result.Observation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Times_out_a_slow_tool_without_throwing()
    {
        var slowTool = new StubAgentTool("slow", async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return ToolExecutionResult.Ok("should never get here");
        });

        var registry = new ToolRegistry([slowTool], DefaultOptions(toolTimeoutSeconds: 1), NullLogger<ToolRegistry>.Instance);

        var result = await registry.ExecuteAsync("slow", default, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("timeout", result.ErrorCategory);
    }

    [Fact]
    public async Task Propagates_real_cancellation_instead_of_reporting_a_timeout()
    {
        var tool = new StubAgentTool("slow", async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return ToolExecutionResult.Ok("unreachable");
        });

        var registry = new ToolRegistry([tool], DefaultOptions(toolTimeoutSeconds: 30), NullLogger<ToolRegistry>.Instance);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<TaskCanceledException>(() => registry.ExecuteAsync("slow", default, cts.Token));
    }

    [Fact]
    public async Task Converts_an_unhandled_tool_exception_into_a_safe_failure()
    {
        var throwingTool = new StubAgentTool("boom", (_, _) => throw new InvalidOperationException("kaboom"));
        var registry = new ToolRegistry([throwingTool], DefaultOptions(), NullLogger<ToolRegistry>.Instance);

        var result = await registry.ExecuteAsync("boom", default, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("tool_exception", result.ErrorCategory);
    }

    [Fact]
    public async Task Truncates_observations_longer_than_the_configured_limit()
    {
        var longObservation = new string('x', 100);
        var tool = StubAgentTool.ReturningOk("verbose", longObservation);
        var registry = new ToolRegistry([tool], DefaultOptions(maxObservationCharacters: 10), NullLogger<ToolRegistry>.Instance);

        var result = await registry.ExecuteAsync("verbose", default, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Observation.Length < longObservation.Length);
        Assert.StartsWith(new string('x', 10), result.Observation, StringComparison.Ordinal);
    }
}
