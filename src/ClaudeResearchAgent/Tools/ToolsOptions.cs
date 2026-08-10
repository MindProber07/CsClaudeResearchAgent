namespace ClaudeResearchAgent.Tools;

/// <summary>Bound from the "Tools:Wikipedia" configuration section.</summary>
public sealed class WikipediaToolOptions
{
    public const string SectionName = "Tools:Wikipedia";

    /// <summary>
    /// Sent as the User-Agent on every request per the Wikimedia API etiquette policy,
    /// which requires an application name and a real contact so operators can reach us.
    /// </summary>
    public string UserAgent { get; set; } =
        "ClaudeResearchAgent/1.0 (https://github.com/anthropics/claude-research-agent; contact@example.com)";

    public int RequestTimeoutSeconds { get; set; } = 10;

    public int MaxRetryAttempts { get; set; } = 3;
}

/// <summary>Bound from the "Tools:Search" configuration section.</summary>
public sealed class WebSearchToolOptions
{
    public const string SectionName = "Tools:Search";

    public int MaxResults { get; set; } = 5;

    public int MaxSnippetCharacters { get; set; } = 400;

    public int RequestTimeoutSeconds { get; set; } = 10;

    public int MaxRetryAttempts { get; set; } = 3;
}

/// <summary>Bound from the "Tools:SaveText" configuration section.</summary>
public sealed class SaveTextToolOptions
{
    public const string SectionName = "Tools:SaveText";

    /// <summary>
    /// The only path this tool will ever write to. Claude supplies content, never a path,
    /// so there is no way for a prompt to redirect writes elsewhere.
    /// </summary>
    public string OutputFilePath { get; set; } = "research_output.txt";
}
