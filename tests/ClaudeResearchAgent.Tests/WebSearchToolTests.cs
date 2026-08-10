using System.Net;
using System.Text;
using System.Text.Json;
using ClaudeResearchAgent.Infrastructure;
using ClaudeResearchAgent.Models;
using ClaudeResearchAgent.Search;
using ClaudeResearchAgent.Tests.TestSupport;
using ClaudeResearchAgent.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ClaudeResearchAgent.Tests;

public class WebSearchToolTests
{
    private static JsonElement Query(string query) => JsonSerializer.SerializeToElement(new { query });

    [Fact]
    public async Task Successful_search_returns_results_and_sources()
    {
        var results = new List<SearchResult>
        {
            new() { Title = "Result 1", Url = "https://example.test/1", Snippet = "First result." },
            new() { Title = "Result 2", Url = "https://example.test/2", Snippet = "Second result." },
        };
        var tool = new WebSearchTool(FakeWebSearchProvider.Returning(results), NullLogger<WebSearchTool>.Instance);

        var result = await tool.ExecuteAsync(Query("sharks"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Sources.Count);
        Assert.Contains("Result 1", result.Observation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_results_still_succeed_with_no_sources()
    {
        var tool = new WebSearchTool(FakeWebSearchProvider.Returning([]), NullLogger<WebSearchTool>.Instance);

        var result = await tool.ExecuteAsync(Query("no results for this"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task Provider_failure_is_reported_as_a_safe_failure()
    {
        var tool = new WebSearchTool(
            FakeWebSearchProvider.Throwing(new HttpRequestException("boom")), NullLogger<WebSearchTool>.Instance);

        var result = await tool.ExecuteAsync(Query("sharks"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("search_provider_failed", result.ErrorCategory);
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_being_swallowed()
    {
        var tool = new WebSearchTool(
            new FakeWebSearchProviderThatRespectsCancellation(), NullLogger<WebSearchTool>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.ExecuteAsync(Query("sharks"), cts.Token));
    }

    [Fact]
    public async Task Missing_query_argument_is_rejected_without_calling_the_provider()
    {
        var providerCalled = false;
        var tool = new WebSearchTool(
            new FakeWebSearchProvider((_, _) => { providerCalled = true; return Task.FromResult<IReadOnlyList<SearchResult>>([]); }),
            NullLogger<WebSearchTool>.Instance);

        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("invalid_argument", result.ErrorCategory);
        Assert.False(providerCalled);
    }

    private sealed class FakeWebSearchProviderThatRespectsCancellation : IWebSearchProvider
    {
        public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }

    // --- DuckDuckGoSearchProvider: bounds unbounded web content before it reaches Claude ---

    [Fact]
    public async Task DuckDuckGo_provider_bounds_result_count_and_snippet_length()
    {
        var relatedTopics = Enumerable.Range(0, 20)
            .Select(i => new { FirstURL = $"https://example.test/{i}", Text = $"Topic {i} - {new string('x', 1000)}" })
            .ToArray();

        var responseJson = JsonSerializer.Serialize(new { RelatedTopics = relatedTopics });

        var innerHandler = new QueueHttpMessageHandler().Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(innerHandler) { BaseAddress = new Uri("https://api.duckduckgo.com/") };
        var factory = new SingleClientHttpClientFactory(httpClient);
        var options = Options.Create(new WebSearchToolOptions { MaxResults = 3, MaxSnippetCharacters = 50 });

        var provider = new DuckDuckGoSearchProvider(factory, options, NullLogger<DuckDuckGoSearchProvider>.Instance);

        var results = await provider.SearchAsync("anything", CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Snippet.Length <= 51)); // +1 for the truncation ellipsis
    }
}
