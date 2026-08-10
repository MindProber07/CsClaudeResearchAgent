namespace ClaudeResearchAgent.Models;

/// <summary>
/// The outcome of a single <see cref="Tools.IAgentTool"/> invocation. <see cref="Observation"/>
/// is the text handed back to Claude as the tool_result content — it is always populated,
/// even on failure, so the model receives a safe, structured explanation instead of a crash.
/// </summary>
public sealed record ToolExecutionResult
{
    public required bool Success { get; init; }

    public required string Observation { get; init; }

    public string? ErrorCategory { get; init; }

    /// <summary>
    /// Sources this tool call actually retrieved. The agent loop uses this — not anything
    /// Claude claims in its final answer — as the authoritative source list.
    /// </summary>
    public IReadOnlyList<ResearchSource> Sources { get; init; } = [];

    public static ToolExecutionResult Ok(string observation, IReadOnlyList<ResearchSource>? sources = null) =>
        new()
        {
            Success = true,
            Observation = observation,
            Sources = sources ?? [],
        };

    public static ToolExecutionResult Fail(string observation, string errorCategory) =>
        new()
        {
            Success = false,
            Observation = observation,
            ErrorCategory = errorCategory,
        };
}
