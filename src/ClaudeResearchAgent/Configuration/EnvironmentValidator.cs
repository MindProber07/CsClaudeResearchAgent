using ClaudeResearchAgent.Agent;

namespace ClaudeResearchAgent.Configuration;

/// <summary>
/// Fails the process fast, before any DI container or HTTP call is built, if the
/// environment is not usable. Startup validation errors are cheap to diagnose;
/// a NullReferenceException three layers into the agent loop is not.
/// </summary>
public static class EnvironmentValidator
{
    public const string ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";

    public static IReadOnlyList<string> Validate(AgentOptions agentOptions)
    {
        var errors = new List<string>(agentOptions.Validate());

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            errors.Add(
                $"Environment variable {ApiKeyEnvironmentVariable} is not set. " +
                "Set it before running (see .env.example); the key is never read from configuration files.");
        }

        return errors;
    }
}
