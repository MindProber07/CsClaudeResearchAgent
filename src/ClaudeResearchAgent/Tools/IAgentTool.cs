using System.Text.Json;
using ClaudeResearchAgent.Models;

namespace ClaudeResearchAgent.Tools;

/// <summary>
/// A capability the research agent can invoke. Implementations are registered by exact name
/// in <see cref="ToolRegistry"/> — the model can never call anything that was not registered.
/// </summary>
public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    /// <summary>
    /// A JSON Schema document (<c>{ "type": "object", "properties": {...}, "required": [...] }</c>)
    /// describing the tool's arguments. Deliberately a plain <see cref="JsonElement"/> rather than
    /// an Anthropic SDK type — the SDK's wire representation is an infrastructure concern, built by
    /// <see cref="Infrastructure.ClaudeMessageAdapter"/> from this schema.
    /// </summary>
    JsonElement InputSchema { get; }

    Task<ToolExecutionResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}
