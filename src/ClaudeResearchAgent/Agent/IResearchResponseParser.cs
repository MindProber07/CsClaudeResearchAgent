using ClaudeResearchAgent.Models;

namespace ClaudeResearchAgent.Agent;

/// <summary>The outcome of one parse attempt: either a validated response, or diagnostics to feed
/// back into a repair prompt.</summary>
public sealed record ResponseParseResult
{
    public ResearchResponse? Response { get; private init; }

    public string? FailureReason { get; private init; }

    public bool Succeeded => Response is not null;

    public static ResponseParseResult Success(ResearchResponse response) => new() { Response = response };

    public static ResponseParseResult Failure(string reason) => new() { FailureReason = reason };
}

/// <summary>
/// Turns Claude's raw final-answer text into a validated <see cref="ResearchResponse"/>. Pure and
/// stateless — it never talks to Claude itself; the agent loop decides what to do with a failure
/// (e.g. ask for a repair).
/// </summary>
public interface IResearchResponseParser
{
    ResponseParseResult Parse(string rawText);
}
