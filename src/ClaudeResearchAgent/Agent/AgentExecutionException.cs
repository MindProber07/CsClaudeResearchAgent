namespace ClaudeResearchAgent.Agent;

/// <summary>The reason a research run stopped without producing a validated answer.</summary>
public enum AgentFailureReason
{
    MaxIterationsExceeded,
    OverallTimeout,
    NoUsableResponse,
    InvalidStructuredOutput,
    Refusal,
    ApiError,
    MissingApiKey,
    ModelUnavailable,
}

/// <summary>
/// Raised whenever the agent loop cannot produce a validated <see cref="Models.ResearchResponse"/>.
/// Every raise site is one of the explicit, bounded failure modes listed in
/// <see cref="AgentFailureReason"/> — this type is never used for an unexpected/unhandled error.
/// </summary>
public sealed class AgentExecutionException : Exception
{
    public AgentExecutionException(AgentFailureReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public AgentExecutionException(AgentFailureReason reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public AgentFailureReason Reason { get; }
}
