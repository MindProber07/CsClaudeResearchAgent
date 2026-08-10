using ClaudeResearchAgent.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaudeResearchAgent.Infrastructure;

/// <summary>Named <see cref="HttpClient"/> registrations for the two external HTTP integrations.</summary>
public static class HttpClientRegistration
{
    public const string WikipediaClientName = "Wikipedia";
    public const string DuckDuckGoClientName = "DuckDuckGo";

    /// <summary>Registers the named "Wikipedia" and "DuckDuckGo" <see cref="HttpClient"/>s, each
    /// with a <see cref="TransientRetryHandler"/> in front and options-driven timeout/User-Agent
    /// configuration.</summary>
    public static IServiceCollection AddClaudeResearchAgentHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient(WikipediaClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<WikipediaToolOptions>>().Value;
                client.BaseAddress = new Uri("https://en.wikipedia.org/w/rest.php/v1/");
                client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            })
            .AddHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<WikipediaToolOptions>>().Value;
                var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<TransientRetryHandler>();
                return new TransientRetryHandler(options.MaxRetryAttempts, logger);
            });

        services.AddHttpClient(DuckDuckGoClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<WebSearchToolOptions>>().Value;
                // The JSON Instant Answer API, not the HTML results page — see
                // DuckDuckGoSearchProvider's remarks for why scraping was avoided.
                client.BaseAddress = new Uri("https://api.duckduckgo.com/");
                client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "ClaudeResearchAgent/1.0 (+https://github.com/anthropics/claude-research-agent)");
            })
            .AddHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<WebSearchToolOptions>>().Value;
                var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<TransientRetryHandler>();
                return new TransientRetryHandler(options.MaxRetryAttempts, logger);
            });

        return services;
    }
}
