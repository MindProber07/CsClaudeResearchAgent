using Anthropic;
using Anthropic.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeResearchAgent.Infrastructure;

/// <summary>
/// Registers the Anthropic SDK client. <see cref="IMessageService"/> — the SDK's own interface for
/// <c>Messages.Create</c> — is what gets injected into <see cref="Agent.ResearchAgent"/>, so tests
/// can substitute a fake implementation without touching HTTP or a real API key.
/// </summary>
public static class AnthropicClientFactory
{
    /// <summary>Registers <see cref="AnthropicClient"/> as a singleton and exposes its
    /// <see cref="IMessageService"/> for injection wherever the agent loop needs to talk to Claude.</summary>
    public static IServiceCollection AddAnthropicClient(this IServiceCollection services)
    {
        // AnthropicClient() reads the ANTHROPIC_API_KEY environment variable itself; the presence
        // check in EnvironmentValidator runs before this is ever constructed so failures surface
        // as a clear startup error rather than a confusing 401 mid-conversation.
        services.AddSingleton<AnthropicClient>();
        services.AddSingleton<IMessageService>(sp => sp.GetRequiredService<AnthropicClient>().Messages);

        return services;
    }
}
