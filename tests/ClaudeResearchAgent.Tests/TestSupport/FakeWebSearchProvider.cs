using ClaudeResearchAgent.Models;
using ClaudeResearchAgent.Search;

namespace ClaudeResearchAgent.Tests.TestSupport;

internal sealed class FakeWebSearchProvider(
    Func<string, CancellationToken, Task<IReadOnlyList<SearchResult>>> search) : IWebSearchProvider
{
    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
        search(query, cancellationToken);

    public static FakeWebSearchProvider Returning(IReadOnlyList<SearchResult> results) =>
        new((_, _) => Task.FromResult(results));

    public static FakeWebSearchProvider Throwing(Exception exception) =>
        new((_, _) => throw exception);
}
