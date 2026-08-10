using ClaudeResearchAgent.Models;

namespace ClaudeResearchAgent.Agent;

/// <summary>
/// The application-side record of what actually happened during a research run: which tools were
/// really invoked, and which sources a tool call really returned. Claude's final JSON answer is
/// reconciled against this — not trusted on its own — for both <c>toolsUsed</c> and <c>sources</c>,
/// because the model can otherwise claim a tool it never called or cite a URL it invented.
/// </summary>
public sealed class ExecutionTracker
{
    private readonly List<string> _executedToolNames = [];
    private readonly Dictionary<string, ResearchSource> _retrievedSources = new(StringComparer.OrdinalIgnoreCase);

    public void RecordSuccessfulExecution(string toolName, IReadOnlyList<ResearchSource> sources)
    {
        _executedToolNames.Add(toolName);
        foreach (var source in sources)
        {
            _retrievedSources[source.Url] = source;
        }
    }

    /// <summary>Distinct tool names actually invoked, in first-use order.</summary>
    public IReadOnlyList<string> ExecutedToolNames => _executedToolNames.Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Keeps only the sources Claude cited that a tool genuinely retrieved, matched by URL.</summary>
    public IReadOnlyList<ResearchSource> ReconcileSources(IReadOnlyList<ResearchSource> claimedSources) =>
        claimedSources.Where(s => _retrievedSources.ContainsKey(s.Url)).ToList();
}
