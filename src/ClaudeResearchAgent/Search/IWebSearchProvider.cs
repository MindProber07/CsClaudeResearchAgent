using ClaudeResearchAgent.Models;

namespace ClaudeResearchAgent.Search;

/// <summary>Abstraction over whichever web-search backend the <c>search</c> tool uses.</summary>
public interface IWebSearchProvider
{
    /// <summary>Runs a search and returns whatever results the backend found — an empty list is a
    /// valid, non-error outcome. Implementations should let failures throw; <c>WebSearchTool</c> is
    /// responsible for turning an exception into a safe <see cref="Models.ToolExecutionResult"/>.</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken);
}
