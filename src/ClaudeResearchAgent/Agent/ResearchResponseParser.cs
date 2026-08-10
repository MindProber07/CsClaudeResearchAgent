using System.Text.Json;
using ClaudeResearchAgent.Models;
using Microsoft.Extensions.Logging;

namespace ClaudeResearchAgent.Agent;

/// <summary>
/// Deserializes and validates Claude's final-answer JSON. Model output is treated as untrusted
/// input at every step: malformed JSON, missing required fields, and structurally invalid sources
/// are all reported as a failure rather than assumed away — see
/// <see cref="ResearchResponseValidation"/> for the field-level rules.
/// </summary>
public sealed class ResearchResponseParser(ILogger<ResearchResponseParser> logger) : IResearchResponseParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ResponseParseResult Parse(string rawText)
    {
        var jsonText = ExtractJsonPayload(rawText);

        ResearchResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<ResearchResponse>(jsonText, SerializerOptions);
        }
        catch (JsonException ex)
        {
            // The original exception is preserved as diagnostic context via structured logging
            // (not swallowed) even though only its message crosses back into the repair prompt.
            logger.LogWarning(ex, "Failed to parse Claude's final answer as JSON.");
            return ResponseParseResult.Failure($"The response is not valid JSON: {ex.Message}");
        }

        if (response is null)
        {
            return ResponseParseResult.Failure("The response deserialized to null.");
        }

        var validationErrors = ResearchResponseValidation.Validate(response);
        if (validationErrors.Count > 0)
        {
            var reason = string.Join(" ", validationErrors);
            logger.LogWarning("Claude's final answer failed validation: {Reason}", reason);
            return ResponseParseResult.Failure(reason);
        }

        return ResponseParseResult.Success(response);
    }

    /// <summary>
    /// Models frequently wrap JSON in a ```json fenced code block even when told not to. Stripping
    /// that here is a defensive convenience, not a trust boundary — the result is still fully
    /// validated afterward.
    /// </summary>
    private static string ExtractJsonPayload(string rawText)
    {
        var trimmed = rawText.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
        {
            return trimmed;
        }

        var withoutOpeningFence = trimmed[(firstNewline + 1)..];
        var closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
        return closingFenceIndex >= 0 ? withoutOpeningFence[..closingFenceIndex].Trim() : withoutOpeningFence.Trim();
    }
}
