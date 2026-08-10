using System.Text.Json;
using Anthropic.Models.Messages;
using ClaudeResearchAgent.Models;
using ClaudeResearchAgent.Tools;

namespace ClaudeResearchAgent.Infrastructure;

/// <summary>
/// The only place in the codebase that translates between our domain types
/// (<see cref="IAgentTool"/>, <see cref="ToolExecutionResult"/>) and the Anthropic SDK's message
/// and content-block types. Keeping every conversion here means the agent loop reads as
/// orchestration logic, not SDK plumbing, and a future SDK shape change only touches this file.
/// </summary>
public static class ClaudeMessageAdapter
{
    public static ToolUnion BuildToolDefinition(IAgentTool tool)
    {
        var sdkTool = new Tool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = ConvertSchema(tool.InputSchema),
        };

        return sdkTool;
    }

    public static MessageParam BuildUserTextMessage(string text) => new() { Role = Role.User, Content = text };

    /// <summary>
    /// Rebuilds the assistant's turn from the response Claude just returned, preserving every
    /// content block — including thinking/redacted_thinking blocks — in original order. The
    /// Claude API requires the exact assistant turn to be echoed back unmodified whenever a
    /// tool_use block is being answered; dropping or reordering blocks (e.g. omitting thinking)
    /// breaks the next turn.
    /// </summary>
    public static MessageParam BuildAssistantMessage(Message response)
    {
        var blocks = new List<ContentBlockParam>();

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var text))
            {
                blocks.Add(new TextBlockParam(text.Text));
            }
            else if (block.TryPickThinking(out var thinking))
            {
                blocks.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
            }
            else if (block.TryPickRedactedThinking(out var redacted))
            {
                blocks.Add(new RedactedThinkingBlockParam(redacted.Data));
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                blocks.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
            }

            // Server-executed block types (web_search_tool_result, code_execution_tool_result, ...)
            // can only appear if a server tool was registered on the request; this agent never
            // registers one, so they are intentionally not handled here.
        }

        return new MessageParam { Role = Role.Assistant, Content = blocks };
    }

    /// <summary>Builds the single user turn carrying every tool_result block for one iteration.</summary>
    public static MessageParam BuildToolResultsMessage(IReadOnlyList<(string ToolUseId, ToolExecutionResult Result)> results)
    {
        var blocks = new List<ContentBlockParam>();

        foreach (var (toolUseId, result) in results)
        {
            ContentBlockParam block = new ToolResultBlockParam(toolUseId)
            {
                Content = result.Observation,
                IsError = !result.Success,
            };
            blocks.Add(block);
        }

        return new MessageParam { Role = Role.User, Content = blocks };
    }

    /// <summary>
    /// Selects tool_use blocks by type, not position — a response can lead with thinking or text
    /// blocks before any tool_use block appears.
    /// </summary>
    public static IReadOnlyList<ToolUseBlock> ExtractToolUseBlocks(Message response)
    {
        var toolUses = new List<ToolUseBlock>();
        foreach (var block in response.Content)
        {
            if (block.TryPickToolUse(out var toolUse))
            {
                toolUses.Add(toolUse);
            }
        }

        return toolUses;
    }

    /// <summary>Finds the first text block anywhere in the response, regardless of position.</summary>
    public static string? ExtractFinalText(Message response)
    {
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var text))
            {
                return text.Text;
            }
        }

        return null;
    }

    private static InputSchema ConvertSchema(JsonElement schemaJson)
    {
        var properties = new Dictionary<string, JsonElement>();
        if (schemaJson.TryGetProperty("properties", out var propertiesElement) &&
            propertiesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in propertiesElement.EnumerateObject())
            {
                properties[property.Name] = property.Value.Clone();
            }
        }

        var required = new List<string>();
        if (schemaJson.TryGetProperty("required", out var requiredElement) &&
            requiredElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in requiredElement.EnumerateArray())
            {
                var name = value.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    required.Add(name);
                }
            }
        }

        return new InputSchema { Properties = properties, Required = required };
    }
}
