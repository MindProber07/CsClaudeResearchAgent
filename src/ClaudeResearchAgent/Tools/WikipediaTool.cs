using System.Net;
using System.Text.Json;
using ClaudeResearchAgent.Infrastructure;
using ClaudeResearchAgent.Models;
using Microsoft.Extensions.Logging;

namespace ClaudeResearchAgent.Tools;

/// <summary>
/// Looks up the most relevant Wikipedia page for a query using the official MediaWiki REST API
/// (<c>/w/rest.php/v1/search/page</c> to find the page, <c>/api/rest_v1/page/summary/</c> for the
/// canonical URL and a concise extract). Retries and backoff for the underlying HTTP calls are
/// handled by <see cref="TransientRetryHandler"/> on the named "Wikipedia" client — this class only
/// concerns itself with request shaping and turning failures into a safe observation.
/// </summary>
public sealed class WikipediaTool(IHttpClientFactory httpClientFactory, ILogger<WikipediaTool> logger) : IAgentTool
{
    public string Name => "wikipedia";

    public string Description =>
        "Searches Wikipedia for the given query and returns a concise summary of the most relevant " +
        "page along with its canonical URL. Use this for encyclopedic background on a person, place, " +
        "event, or concept.";

    public JsonElement InputSchema => ToolSchemas.SingleRequiredStringProperty(
        "query", "The topic or question to look up on Wikipedia.");

    public async Task<ToolExecutionResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetRequiredString(arguments, "query", out var query))
        {
            return Failure("invalid_argument", "The wikipedia tool requires a non-empty 'query' string argument.");
        }

        var client = httpClientFactory.CreateClient(HttpClientRegistration.WikipediaClientName);

        try
        {
            var page = await FindPageAsync(client, query, cancellationToken).ConfigureAwait(false);
            if (page is null)
            {
                return Failure("not_found", $"No Wikipedia page was found for '{query}'.");
            }

            var summary = await GetSummaryAsync(client, page.Value.Key, cancellationToken).ConfigureAwait(false);
            if (summary is null)
            {
                return Failure("not_found", $"Wikipedia page '{page.Value.Title}' has no retrievable summary.");
            }

            var result = new WikipediaLookupResult
            {
                Success = true,
                Title = summary.Value.Title,
                Url = summary.Value.Url,
                Summary = summary.Value.Extract,
            };

            var source = new ResearchSource { Title = result.Title!, Url = result.Url!, Excerpt = result.Summary };
            return ToolExecutionResult.Ok(Serialize(result), [source]);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Wikipedia lookup for '{Query}' failed with a network error.", query);
            return Failure("network_error", $"Wikipedia lookup failed: {ex.Message}");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Wikipedia lookup for '{Query}' returned an unparseable response.", query);
            return Failure("invalid_response", "Wikipedia returned an unparseable response.");
        }
    }

    private static async Task<(string Key, string Title)?> FindPageAsync(
        HttpClient client, string query, CancellationToken cancellationToken)
    {
        var requestUri = "https://en.wikipedia.org/w/rest.php/v1/search/page?q="
            + Uri.EscapeDataString(query) + "&limit=1";

        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("pages", out var pages) ||
            pages.ValueKind != JsonValueKind.Array ||
            pages.GetArrayLength() == 0)
        {
            return null;
        }

        var firstPage = pages[0];
        var key = firstPage.GetProperty("key").GetString();
        var title = firstPage.GetProperty("title").GetString();

        return string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(title)
            ? null
            : (key, title);
    }

    private static async Task<(string Title, string Url, string Extract)?> GetSummaryAsync(
        HttpClient client, string pageKey, CancellationToken cancellationToken)
    {
        var requestUri = "https://en.wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(pageKey);

        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
        var extract = root.TryGetProperty("extract", out var extractEl) ? extractEl.GetString() : null;

        string? canonicalUrl = null;
        if (root.TryGetProperty("content_urls", out var contentUrls) &&
            contentUrls.TryGetProperty("desktop", out var desktop) &&
            desktop.TryGetProperty("page", out var pageUrl))
        {
            canonicalUrl = pageUrl.GetString();
        }

        return string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(canonicalUrl) || string.IsNullOrWhiteSpace(extract)
            ? null
            : (title, canonicalUrl, extract);
    }

    private static ToolExecutionResult Failure(string errorCategory, string message)
    {
        var result = new WikipediaLookupResult { Success = false, ErrorCategory = errorCategory };
        return ToolExecutionResult.Fail(Serialize(result) + " " + message, errorCategory);
    }

    private static string Serialize(WikipediaLookupResult result) => JsonSerializer.Serialize(result);
}
