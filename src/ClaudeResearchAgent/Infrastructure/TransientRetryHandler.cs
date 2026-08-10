using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace ClaudeResearchAgent.Infrastructure;

/// <summary>
/// A <see cref="DelegatingHandler"/> that retries transient HTTP failures (timeouts, 429,
/// 5xx) with bounded exponential backoff and jitter, honoring a server-supplied
/// <c>Retry-After</c> header when present. Permanent 4xx responses are never retried.
/// Built on Polly (.NET's recommended resilience library) rather than a hand-rolled loop
/// so the retry semantics stay well-tested and reviewable.
/// </summary>
/// <remarks>
/// Constructed directly (rather than only through <c>AddResilienceHandler</c> DI wiring)
/// so tests can attach it in front of a fake inner handler without spinning up a DI container.
/// </remarks>
public sealed class TransientRetryHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    /// <param name="maxRetryAttempts">Upper bound on retries after the initial attempt — total
    /// attempts are <c>maxRetryAttempts + 1</c>.</param>
    /// <param name="logger">Receives one warning per retry; never receives the final failure (the
    /// caller logs that, since only it knows the tool-level context).</param>
    public TransientRetryHandler(int maxRetryAttempts, ILogger logger)
    {
        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new HttpRetryStrategyOptions
            {
                ShouldHandle = args => ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome)),
                ShouldRetryAfterHeader = true,
                MaxRetryAttempts = maxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(250),
                MaxDelay = TimeSpan.FromSeconds(10),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Transient HTTP failure on attempt {Attempt} (status {StatusCode}); retrying in {Delay}.",
                        args.AttemptNumber + 1,
                        args.Outcome.Result?.StatusCode,
                        args.RetryDelay);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async ct => await base.SendAsync(request, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }
}
