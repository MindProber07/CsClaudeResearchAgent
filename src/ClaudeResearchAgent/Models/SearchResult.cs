namespace ClaudeResearchAgent.Models;

/// <summary>A single web search hit returned by an <see cref="Search.IWebSearchProvider"/>.</summary>
public sealed record SearchResult
{
    public required string Title { get; init; }

    public required string Url { get; init; }

    public required string Snippet { get; init; }
}
