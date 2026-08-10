namespace ClaudeResearchAgent.Agent;

/// <summary>Bound from the "Agent" configuration section. Every execution limit lives here so
/// none of it is scattered through the orchestration code as magic numbers.</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public required string Model { get; set; }

    public int MaxIterations { get; set; } = 8;

    public int OverallTimeoutSeconds { get; set; } = 120;

    public int ToolTimeoutSeconds { get; set; } = 30;

    public int MaximumToolResultCharacters { get; set; } = 12_000;

    public int MaximumSaveCharacters { get; set; } = 50_000;

    public int MaximumFormatRepairAttempts { get; set; } = 1;

    public int MaxTokens { get; set; } = 4096;

    /// <summary>Returns every invalid setting as a human-readable message — empty when every limit
    /// is usable. Called from <see cref="Configuration.EnvironmentValidator"/> before the DI
    /// container is built.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Model))
        {
            errors.Add("Agent:Model must be configured.");
        }

        if (MaxIterations < 1)
        {
            errors.Add("Agent:MaxIterations must be at least 1.");
        }

        if (OverallTimeoutSeconds < 1)
        {
            errors.Add("Agent:OverallTimeoutSeconds must be at least 1.");
        }

        if (ToolTimeoutSeconds < 1)
        {
            errors.Add("Agent:ToolTimeoutSeconds must be at least 1.");
        }

        if (MaximumToolResultCharacters < 1)
        {
            errors.Add("Agent:MaximumToolResultCharacters must be at least 1.");
        }

        if (MaximumSaveCharacters < 1)
        {
            errors.Add("Agent:MaximumSaveCharacters must be at least 1.");
        }

        if (MaximumFormatRepairAttempts < 0)
        {
            errors.Add("Agent:MaximumFormatRepairAttempts cannot be negative.");
        }

        if (MaxTokens < 1)
        {
            errors.Add("Agent:MaxTokens must be at least 1.");
        }

        return errors;
    }
}
