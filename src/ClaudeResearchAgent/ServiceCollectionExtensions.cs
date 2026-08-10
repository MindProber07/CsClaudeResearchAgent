using ClaudeResearchAgent.Agent;
using ClaudeResearchAgent.Infrastructure;
using ClaudeResearchAgent.Search;
using ClaudeResearchAgent.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClaudeResearchAgent;

/// <summary>Composition root: wires every layer (config, HTTP, tools, Claude client, agent) together.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClaudeResearchAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WikipediaToolOptions>(configuration.GetSection(WikipediaToolOptions.SectionName));
        services.Configure<WebSearchToolOptions>(configuration.GetSection(WebSearchToolOptions.SectionName));
        services.Configure<SaveTextToolOptions>(configuration.GetSection(SaveTextToolOptions.SectionName));

        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AgentOptions>>().Value);

        services.AddClaudeResearchAgentHttpClients();
        services.AddAnthropicClient();

        services.AddSingleton<IWebSearchProvider, DuckDuckGoSearchProvider>();

        services.AddSingleton<IAgentTool, WikipediaTool>();
        services.AddSingleton<IAgentTool, WebSearchTool>();
        services.AddSingleton<IAgentTool, SaveTextTool>();
        services.AddSingleton<ToolRegistry>();

        services.AddSingleton<IResearchResponseParser, ResearchResponseParser>();
        services.AddSingleton<IResearchAgent, ResearchAgent>();

        return services;
    }
}
