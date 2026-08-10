using System.Text.Json;

namespace ClaudeResearchAgent.Tools;

/// <summary>Small helper for pulling arguments out of the raw <see cref="JsonElement"/> Claude sends
/// with a tool_use block, without every tool re-implementing the same defensive checks.</summary>
internal static class ToolArguments
{
    public static bool TryGetRequiredString(JsonElement arguments, string propertyName, out string value)
    {
        value = string.Empty;

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!arguments.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text;
        return true;
    }
}
