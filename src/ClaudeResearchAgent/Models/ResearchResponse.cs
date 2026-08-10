namespace ClaudeResearchAgent.Models;

/// <summary>
/// The strongly typed final answer produced by the research agent. An instance of
/// this type is only ever handed to the console layer after it has passed
/// <see cref="ResearchResponseValidation.Validate"/> — Claude's raw JSON output is
/// untrusted until then.
/// </summary>
public sealed record ResearchResponse
{
    public required string Topic { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<ResearchSource> Sources { get; init; }

    public required IReadOnlyList<string> ToolsUsed { get; init; }
}

public static class ResearchResponseValidation
{
    /// <summary>
    /// Checks structural validity only. Sources with invalid URLs are not rejected here —
    /// the agent loop strips those out earlier by cross-checking against sources it actually
    /// retrieved, so by the time this runs every source is expected to already be legitimate.
    /// </summary>
    public static IReadOnlyList<string> Validate(ResearchResponse response)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(response.Topic))
        {
            errors.Add("Topic must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(response.Summary))
        {
            errors.Add("Summary must not be empty.");
        }

        for (var i = 0; i < response.Sources.Count; i++)
        {
            if (!ResearchSourceValidation.IsValid(response.Sources[i]))
            {
                errors.Add($"Sources[{i}] has an empty title or an invalid absolute http/https URL.");
            }
        }

        return errors;
    }
}
