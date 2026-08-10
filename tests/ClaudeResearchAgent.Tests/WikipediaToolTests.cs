using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaudeResearchAgent.Infrastructure;
using ClaudeResearchAgent.Tests.TestSupport;
using ClaudeResearchAgent.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClaudeResearchAgent.Tests;

public class WikipediaToolTests
{
    private const string UserAgent = "ClaudeResearchAgent/1.0 (https://example.test; contact@example.test)";

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static readonly string SearchResponseJson = JsonSerializer.Serialize(new
    {
        pages = new[] { new { id = 1, key = "Hammerhead_shark", title = "Hammerhead shark" } },
    });

    private static readonly string SummaryResponseJson = JsonSerializer.Serialize(new
    {
        title = "Hammerhead shark",
        extract = "Hammerhead sharks are a family of sharks known for their distinctively shaped heads.",
        content_urls = new { desktop = new { page = "https://en.wikipedia.org/wiki/Hammerhead_shark" } },
    });

    private static (WikipediaTool Tool, QueueHttpMessageHandler Inner) CreateTool(int maxRetryAttempts = 3)
    {
        var inner = new QueueHttpMessageHandler();
        var retryHandler = new TransientRetryHandler(maxRetryAttempts, NullLogger.Instance) { InnerHandler = inner };
        var httpClient = new HttpClient(retryHandler);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        var factory = new SingleClientHttpClientFactory(httpClient);
        var tool = new WikipediaTool(factory, NullLogger<WikipediaTool>.Instance);
        return (tool, inner);
    }

    private static JsonElement Query(string query) =>
        JsonSerializer.SerializeToElement(new { query });

    [Fact]
    public async Task Successful_lookup_returns_title_url_and_summary()
    {
        var (tool, inner) = CreateTool();
        inner.Enqueue(JsonResponse(HttpStatusCode.OK, SearchResponseJson));
        inner.Enqueue(JsonResponse(HttpStatusCode.OK, SummaryResponseJson));

        var result = await tool.ExecuteAsync(Query("hammerhead shark"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.Sources);
        Assert.Equal("https://en.wikipedia.org/wiki/Hammerhead_shark", result.Sources[0].Url);
        Assert.Contains("Hammerhead shark", result.Observation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_search_result_reports_not_found_without_a_second_call()
    {
        var (tool, inner) = CreateTool();
        inner.Enqueue(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new { pages = Array.Empty<object>() })));

        var result = await tool.ExecuteAsync(Query("asdkjhqwekjhasdkjh"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("not_found", result.ErrorCategory);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Retries_after_a_429_then_succeeds()
    {
        var (tool, inner) = CreateTool();
        var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        inner.Enqueue(rateLimited);
        inner.Enqueue(JsonResponse(HttpStatusCode.OK, SearchResponseJson));
        inner.Enqueue(JsonResponse(HttpStatusCode.OK, SummaryResponseJson));

        var result = await tool.ExecuteAsync(Query("hammerhead shark"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(3, inner.Requests.Count);
    }

    [Fact]
    public async Task Repeated_transient_failures_eventually_report_a_safe_failure()
    {
        var (tool, inner) = CreateTool(maxRetryAttempts: 2);
        for (var i = 0; i < 3; i++)
        {
            inner.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        var result = await tool.ExecuteAsync(Query("hammerhead shark"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("network_error", result.ErrorCategory);
        Assert.Equal(3, inner.Requests.Count); // 1 initial attempt + 2 retries, then give up
    }

    [Fact]
    public async Task Permanent_client_error_is_not_retried()
    {
        var (tool, inner) = CreateTool(maxRetryAttempts: 3);
        inner.Enqueue(new HttpResponseMessage(HttpStatusCode.BadRequest));

        var result = await tool.ExecuteAsync(Query("hammerhead shark"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Single(inner.Requests); // no retries for a permanent 4xx
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_being_swallowed()
    {
        var (tool, _) = CreateTool();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.ExecuteAsync(Query("hammerhead shark"), cts.Token));
    }

    [Fact]
    public async Task Sends_the_configured_custom_user_agent()
    {
        var (tool, inner) = CreateTool();
        inner.Enqueue(JsonResponse(HttpStatusCode.OK, SearchResponseJson));
        inner.Enqueue(JsonResponse(HttpStatusCode.OK, SummaryResponseJson));

        await tool.ExecuteAsync(Query("hammerhead shark"), CancellationToken.None);

        Assert.All(inner.Requests, request =>
            Assert.Equal(UserAgent, request.Headers.UserAgent.ToString()));
    }
}
