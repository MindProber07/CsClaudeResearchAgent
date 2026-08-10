using System.Text.Json;
using ClaudeResearchAgent.Models;
using ClaudeResearchAgent.Tools;

namespace ClaudeResearchAgent.Tests.TestSupport;

/// <summary>A minimal, fully controllable <see cref="IAgentTool"/> for exercising the tool
/// registry and agent loop without any real tool implementation.</summary>
internal sealed class StubAgentTool(
    string name,
    Func<JsonElement, CancellationToken, Task<ToolExecutionResult>> execute) : IAgentTool
{
    public int CallCount { get; private set; }

    public string Name { get; } = name;

    public string Description => $"Stub tool '{Name}' for testing.";

    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new Dictionary<string, object>(),
        required = Array.Empty<string>(),
    });

    public Task<ToolExecutionResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        CallCount++;
        return execute(arguments, cancellationToken);
    }

    public static StubAgentTool ReturningOk(string name, string observation, IReadOnlyList<ResearchSource>? sources = null) =>
        new(name, (_, _) => Task.FromResult(ToolExecutionResult.Ok(observation, sources)));
}
