using ClaudeResearchAgent.Agent;
using ClaudeResearchAgent.Models;
using Microsoft.Extensions.Logging;

namespace ClaudeResearchAgent.ConsoleUi;

/// <summary>The interactive read-question/run-agent/print-answer loop. Kept separate from
/// <c>Program.cs</c> so the host bootstrap file stays small.</summary>
public sealed class ResearchConsoleSession(IResearchAgent agent, ILogger<ResearchConsoleSession> logger)
{
    private static readonly string[] ExitCommands = ["exit", "quit"];

    /// <summary>Runs until the user asks to exit or cancellation is requested. Returns the process
    /// exit code.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        PrintBanner();

        var sawAnyFailure = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("\nResearch question> ");
            var input = Console.ReadLine();

            if (input is null)
            {
                // Redirected/closed stdin (e.g. piped input exhausted) — stop rather than spin.
                break;
            }

            var trimmed = input.Trim();
            if (trimmed.Length == 0)
            {
                Console.WriteLine("Please enter a research question, or type 'exit' to quit.");
                continue;
            }

            if (ExitCommands.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                break;
            }

            try
            {
                var response = await agent.ResearchAsync(trimmed, cancellationToken).ConfigureAwait(false);
                PrintResponse(response);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("\nCancelled.");
                break;
            }
            catch (AgentExecutionException ex)
            {
                sawAnyFailure = true;
                logger.LogError("Research failed ({Reason}): {Message}", ex.Reason, ex.Message);
                Console.WriteLine($"\nCould not complete this research request: {ex.Message}");
            }
        }

        Console.WriteLine("\nGoodbye.");
        return sawAnyFailure ? 1 : 0;
    }

    private static void PrintBanner()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine(" Claude Research Agent");
        Console.WriteLine("=================================================");
        Console.WriteLine("Enter a research question, or type 'exit'/'quit' to leave.");
    }

    private static void PrintResponse(ResearchResponse response)
    {
        Console.WriteLine();
        Console.WriteLine("Topic:");
        Console.WriteLine(response.Topic);

        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine(response.Summary);

        Console.WriteLine();
        Console.WriteLine("Sources:");
        if (response.Sources.Count == 0)
        {
            Console.WriteLine("(none)");
        }
        else
        {
            for (var i = 0; i < response.Sources.Count; i++)
            {
                var source = response.Sources[i];
                Console.WriteLine($"{i + 1}. {source.Title}");
                Console.WriteLine($"   {source.Url}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Tools used:");
        Console.WriteLine(response.ToolsUsed.Count == 0 ? "(none)" : string.Join(", ", response.ToolsUsed));
    }
}
