using System.Text.Json;
using ClaudeResearchAgent.Infrastructure;
using ClaudeResearchAgent.Models;
using ClaudeResearchAgent.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaudeResearchAgent.Search;

/// <summary>
/// Web search backed by the DuckDuckGo Instant Answer API (<c>api.duckduckgo.com/?format=json</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Known limitation:</b> this is DuckDuckGo's documented JSON "Instant Answer" endpoint, not a
/// general-purpose ranked web search index. It reliably returns a result for topics that have a
/// Wikipedia-style abstract or a disambiguation/related-topics list (people, places, concepts,
/// organizations) but frequently returns few or zero results for narrow, current-events, or
/// long-tail queries. It was chosen over scraping DuckDuckGo's HTML results page because that page
/// has no stability contract, can change layout without notice, and scraping it may violate terms
/// of use. See the README "Known limitations" section for the full rationale and alternatives
/// (e.g. swapping in a paid search API) if broader coverage is needed.
/// </para>
/// </remarks>
public sealed class DuckDuckGoSearchProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<WebSearchToolOptions> options,
    ILogger<DuckDuckGoSearchProvider> logger) : IWebSearchProvider
{
    private readonly WebSearchToolOptions _options = options.Value;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var client = httpClientFactory.CreateClient(HttpClientRegistration.DuckDuckGoClientName);
        var requestUri = "?q=" + Uri.EscapeDataString(query) + "&format=json&no_redirect=1&no_html=1&skip_disambig=1";

        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var results = new List<SearchResult>();
        AddAbstractResult(document.RootElement, results);
        AddRelatedTopics(document.RootElement, results);

        if (results.Count == 0)
        {
            logger.LogInformation("DuckDuckGo Instant Answer API returned no usable results for query {Query}.", query);
        }

        return results
            .Take(_options.MaxResults)
            .Select(r => r with { Snippet = Truncate(r.Snippet, _options.MaxSnippetCharacters) })
            .ToList();
    }

    private static void AddAbstractResult(JsonElement root, List<SearchResult> results)
    {
        var heading = GetString(root, "Heading");
        var abstractUrl = GetString(root, "AbstractURL");
        var abstractText = GetString(root, "AbstractText");

        if (!string.IsNullOrWhiteSpace(heading) && !string.IsNullOrWhiteSpace(abstractUrl))
        {
            results.Add(new SearchResult
            {
                Title = heading,
                Url = abstractUrl,
                Snippet = abstractText ?? string.Empty,
            });
        }
    }

    private static void AddRelatedTopics(JsonElement root, List<SearchResult> results)
    {
        if (!root.TryGetProperty("RelatedTopics", out var relatedTopics) || relatedTopics.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var topic in relatedTopics.EnumerateArray())
        {
            // Category groupings nest their own "Topics" array instead of exposing FirstURL directly.
            if (topic.TryGetProperty("Topics", out var nested) && nested.ValueKind == JsonValueKind.Array)
            {
                foreach (var nestedTopic in nested.EnumerateArray())
                {
                    TryAddTopic(nestedTopic, results);
                }

                continue;
            }

            TryAddTopic(topic, results);
        }
    }

    private static void TryAddTopic(JsonElement topic, List<SearchResult> results)
    {
        var url = GetString(topic, "FirstURL");
        var text = GetString(topic, "Text");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // DuckDuckGo's "Text" field doubles as title + snippet ("Title - description"); split on the
        // first " - " when present so callers get a real title instead of the whole sentence twice.
        var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
        var title = separatorIndex > 0 ? text[..separatorIndex] : text;

        results.Add(new SearchResult
        {
            Title = title,
            Url = url,
            Snippet = text,
        });
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "…");
}
