namespace ClaudeResearchAgent.Tests.TestSupport;

/// <summary>A fake HTTP transport: each call to <see cref="SendAsync"/> dequeues and runs the next
/// configured responder. Used as the innermost handler behind <c>TransientRetryHandler</c> so
/// retry tests can assert exactly how many requests actually went out.</summary>
internal sealed class QueueHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public QueueHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responders.Enqueue(responder);
        return this;
    }

    public QueueHttpMessageHandler Enqueue(HttpResponseMessage response) => Enqueue(_ => response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException(
                $"QueueHttpMessageHandler received request #{Requests.Count} for {request.RequestUri} " +
                "but no more stub responses were enqueued.");
        }

        return Task.FromResult(_responders.Dequeue()(request));
    }
}
