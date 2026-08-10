using ClaudeResearchAgent;
using ClaudeResearchAgent.Agent;
using ClaudeResearchAgent.Configuration;
using ClaudeResearchAgent.ConsoleUi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Development convenience only — real deployments set ANTHROPIC_API_KEY as an actual
// environment variable, which is what EnvironmentValidator below actually checks.
DotEnvLoader.LoadIfPresent();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Validated before the DI container is even built: a bad config should fail with a clear
// message, not surface as a confusing null-reference three layers into the agent loop.
var agentOptions = builder.Configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>()
    ?? new AgentOptions { Model = string.Empty };

var validationErrors = EnvironmentValidator.Validate(agentOptions);
if (validationErrors.Count > 0)
{
    Console.Error.WriteLine("Configuration is invalid:");
    foreach (var error in validationErrors)
    {
        Console.Error.WriteLine($"  - {error}");
    }

    return 1;
}

builder.Services.AddClaudeResearchAgent(builder.Configuration);
builder.Services.AddSingleton<ResearchConsoleSession>();

using var host = builder.Build();

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // Let the console loop unwind on its own rather than letting the runtime hard-kill the process.
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var session = host.Services.GetRequiredService<ResearchConsoleSession>();

try
{
    return await session.RunAsync(cancellationSource.Token).ConfigureAwait(false);
}
catch (Exception ex)
{
    host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("ClaudeResearchAgent.Program")
        .LogCritical(ex, "Fatal error while running the research agent.");
    return 1;
}
