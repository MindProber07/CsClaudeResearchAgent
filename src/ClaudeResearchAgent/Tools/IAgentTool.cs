using System.Text.Json;
using ClaudeResearchAgent.Models;

namespace ClaudeResearchAgent.Tools;

/// <summary>
/// A capability the research agent can invoke. Implementations are registered by exact name
/// in <see cref="ToolRegistry"/> — the model can never call anything that was not registered.
/// </summary>
public interface IAgentTool
{
    /// <summary>The exact tool name Claude must use in a tool_use block, and the key
    /// <see cref="ToolRegistry"/> registers this implementation under.</summary>
    string Name { get; }

    /// <summary>Sent to Claude as the tool's description — this is what the model reads to decide
    /// whether and how to call the tool, so it should be precise about purpose and limitations.</summary>
    string Description { get; }

    /// <summary>
    /// A JSON Schema document (<c>{ "type": "object", "properties": {...}, "required": [...] }</c>)
    /// describing the tool's arguments. Deliberately a plain <see cref="JsonElement"/> rather than
    /// an Anthropic SDK type — the SDK's wire representation is an infrastructure concern, built by
    /// <see cref="Infrastructure.ClaudeMessageAdapter"/> from this schema.
    /// </summary>
    JsonElement InputSchema { get; }

    /// <summary>
    /// Runs the tool. Must never throw for expected failure modes (bad arguments, network errors,
    /// ...) — those should come back as <see cref="ToolExecutionResult.Fail"/> so the agent loop can
    /// hand a safe observation back to Claude. <see cref="ToolRegistry"/> already applies a timeout
    /// and unexpected-exception guard around every call, so this is a second line of defense.
    /// </summary>
    Task<ToolExecutionResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}
