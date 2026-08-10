using System.Text.Json;
using Anthropic.Models.Messages;

namespace ClaudeResearchAgent.Tests.TestSupport;

/// <summary>
/// Builds Anthropic SDK <see cref="Message"/>/<see cref="ContentBlock"/> instances for tests.
/// Every <c>required</c> member the SDK's generated models demand gets a harmless placeholder
/// value here so test code only has to specify what actually matters for the scenario.
/// </summary>
internal static class AnthropicMessageFactory
{
    public static ContentBlock Text(string text)
    {
        TextBlock block = new() { Text = text, Citations = null };
        return block;
    }

    public static ContentBlock ToolUse(string id, string name, object input)
    {
        var inputElement = JsonSerializer.SerializeToElement(input);
        var inputDictionary = inputElement.ValueKind == JsonValueKind.Object
            ? inputElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : new Dictionary<string, JsonElement>();

        ToolUseBlock block = new()
        {
            ID = id,
            Name = name,
            Input = inputDictionary,
            Caller = new DirectCaller(),
        };
        return block;
    }

    public static ContentBlock Thinking(string thinking, string signature = "test-signature")
    {
        ThinkingBlock block = new() { Thinking = thinking, Signature = signature };
        return block;
    }

    public static ContentBlock RedactedThinking(string data)
    {
        RedactedThinkingBlock block = new(data);
        return block;
    }

    public static Message Build(StopReason stopReason, params ContentBlock[] content) => new()
    {
        ID = "msg_" + Guid.NewGuid().ToString("N")[..8],
        Container = null,
        Model = "claude-sonnet-5",
        StopDetails = null,
        StopSequence = null,
        Usage = null!,
        Content = content.ToList(),
        StopReason = stopReason,
    };

    public static Message BuildRefusal(string explanation) => new()
    {
        ID = "msg_" + Guid.NewGuid().ToString("N")[..8],
        Container = null,
        Model = "claude-sonnet-5",
        StopDetails = new RefusalStopDetails { Category = Category.GeneralHarms, Explanation = explanation },
        StopSequence = null,
        Usage = null!,
        Content = [],
        StopReason = StopReason.Refusal,
    };
}
