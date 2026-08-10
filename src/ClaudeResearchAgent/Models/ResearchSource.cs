namespace ClaudeResearchAgent.Models;

/// <summary>
/// A single citation backing a <see cref="ResearchResponse"/>. Instances are only
/// considered valid once they have passed <see cref="ResearchSourceValidation"/>.
/// </summary>
public sealed record ResearchSource
{
    public required string Title { get; init; }

    public required string Url { get; init; }

    public string? Excerpt { get; init; }
}

public static class ResearchSourceValidation
{
    /// <summary>
    /// A source is only trustworthy if it has a real title and an absolute http(s) URL.
    /// Model-generated URLs are treated as untrusted until they pass this check.
    /// </summary>
    public static bool IsValid(ResearchSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Title))
        {
            return false;
        }

        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https";
    }
}
