using System.Text;
using System.Text.Json;
using ClaudeResearchAgent.Agent;
using ClaudeResearchAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaudeResearchAgent.Tools;

/// <summary>
/// Appends research content to a single, operator-configured local file. Claude supplies content
/// only — never a path — so there is no way for a prompt to redirect writes anywhere else on disk.
/// </summary>
public sealed class SaveTextTool(
    IOptions<SaveTextToolOptions> saveOptions,
    IOptions<AgentOptions> agentOptions,
    ILogger<SaveTextTool> logger) : IAgentTool
{
    // Process-wide: this tool only ever targets one configured file, so a single semaphore is
    // enough to serialize concurrent tool_use calls without a per-path lock table.
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public string Name => "save_text_to_file";

    public string Description =>
        "Appends the given research content, with a timestamp, to a local research notes file. " +
        "Use this to preserve findings the user may want to review later. You cannot choose the " +
        "file path — content is always appended to the single configured output file.";

    public JsonElement InputSchema => ToolSchemas.SingleRequiredStringProperty(
        "content", "The research content to append to the output file.");

    public async Task<ToolExecutionResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetRequiredString(arguments, "content", out var content))
        {
            return ToolExecutionResult.Fail(
                "The save_text_to_file tool requires a non-empty 'content' string argument.", "invalid_argument");
        }

        var maxChars = agentOptions.Value.MaximumSaveCharacters;
        if (content.Length > maxChars)
        {
            logger.LogWarning("Rejected save_text_to_file request: {Length} characters exceeds the {Max} limit.", content.Length, maxChars);
            return ToolExecutionResult.Fail(
                JsonSerializer.Serialize(new { success = false, error = "content_too_large" }) +
                $" Content length {content.Length} exceeds the maximum of {maxChars} characters.",
                "content_too_large");
        }

        var path = saveOptions.Value.OutputFilePath;
        var timestamp = DateTimeOffset.UtcNow;
        var entry = $"{new string('-', 40)}{Environment.NewLine}[{timestamp:O}]{Environment.NewLine}{content}{Environment.NewLine}";

        await WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, entry, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            WriteLock.Release();
        }

        logger.LogInformation("Appended {Length} characters to {Path}.", content.Length, path);
        return ToolExecutionResult.Ok(JsonSerializer.Serialize(new
        {
            success = true,
            path,
            savedAt = timestamp,
            characters = content.Length,
        }));
    }
}
