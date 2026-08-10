using ClaudeResearchAgent.Models;

namespace ClaudeResearchAgent.Agent;

/// <summary>The outcome of one parse attempt: either a validated response, or diagnostics to feed
/// back into a repair prompt.</summary>
public sealed record ResponseParseResult
{
    /// <summary>The validated response, when <see cref="Succeeded"/> is <see langword="true"/>;
    /// otherwise <see langword="null"/>.</summary>
    public ResearchResponse? Response { get; private init; }

    /// <summary>Human-readable parse/validation failure detail, suitable for feeding back into a
    /// repair prompt. <see langword="null"/> when <see cref="Succeeded"/> is <see langword="true"/>.</summary>
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
    /// <summary>Attempts to parse and validate Claude's final-answer text as a
    /// <see cref="ResearchResponse"/>. Never throws — a malformed or invalid response comes back as
    /// a failed <see cref="ResponseParseResult"/>, not an exception.</summary>
    ResponseParseResult Parse(string rawText);
}
