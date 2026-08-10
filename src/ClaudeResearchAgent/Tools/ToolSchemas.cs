using System.Text.Json;

namespace ClaudeResearchAgent.Tools;

/// <summary>Builds the small JSON Schema documents each tool exposes via <see cref="IAgentTool.InputSchema"/>.</summary>
internal static class ToolSchemas
{
    /// <summary>Builds a JSON Schema object with exactly one required string property — the shape
    /// shared by all three of this project's tools.</summary>
    public static JsonElement SingleRequiredStringProperty(string propertyName, string propertyDescription)
    {
        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                [propertyName] = new { type = "string", description = propertyDescription },
            },
            required = new[] { propertyName },
        };

        return JsonSerializer.SerializeToElement(schema);
    }
}
