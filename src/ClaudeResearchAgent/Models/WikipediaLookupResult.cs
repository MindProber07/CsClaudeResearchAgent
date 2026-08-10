namespace ClaudeResearchAgent.Models;

/// <summary>
/// The structured payload the wikipedia tool serializes into its tool_result observation.
/// Kept as a real type (rather than building JSON ad hoc) so the shape sent to Claude is
/// predictable and unit-testable.
/// </summary>
public sealed record WikipediaLookupResult
{
    public required bool Success { get; init; }

    public string? Title { get; init; }

    public string? Url { get; init; }

    public string? Summary { get; init; }

    public string? ErrorCategory { get; init; }
}
