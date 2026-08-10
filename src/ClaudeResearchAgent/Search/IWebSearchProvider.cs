using ClaudeResearchAgent.Models;

namespace ClaudeResearchAgent.Search;

/// <summary>Abstraction over whichever web-search backend the <c>search</c> tool uses.</summary>
public interface IWebSearchProvider
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken);
}
