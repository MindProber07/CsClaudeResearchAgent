using ClaudeResearchAgent.Models;

namespace ClaudeResearchAgent.Agent;

/// <summary>
/// Runs the full tool-calling research loop for one question and returns a validated answer.
/// Throws <see cref="AgentExecutionException"/> for every bounded failure mode (limits reached,
/// invalid output, refusal, missing key, ...) — callers do not need to guard against unbounded
/// execution or partially-formed results.
/// </summary>
public interface IResearchAgent
{
    Task<ResearchResponse> ResearchAsync(string question, CancellationToken cancellationToken);
}
