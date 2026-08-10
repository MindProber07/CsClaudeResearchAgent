using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Anthropic.Services;
using Anthropic.Services.Messages;

namespace ClaudeResearchAgent.Tests.TestSupport;

/// <summary>
/// A hand-written fake of the SDK's own <see cref="IMessageService"/> interface — no live API call,
/// no mocking framework needed for the parts we exercise. Only <see cref="Create"/> is used by
/// <see cref="Agent.ResearchAgent"/>; the rest of the interface throws so an accidental call is
/// loud rather than silently returning nonsense.
/// </summary>
internal sealed class FakeMessageService : IMessageService
{
    private readonly Queue<Func<MessageCreateParams, Message>> _responses = new();

    public List<MessageCreateParams> Requests { get; } = [];

    public FakeMessageService Enqueue(Func<MessageCreateParams, Message> responder)
    {
        _responses.Enqueue(responder);
        return this;
    }

    public FakeMessageService Enqueue(Message message) => Enqueue(_ => message);

    public Task<Message> Create(MessageCreateParams parameters, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(parameters);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"FakeMessageService.Create was called {Requests.Count} time(s) but only " +
                $"{Requests.Count - 1} canned response(s) were enqueued.");
        }

        return Task.FromResult(_responses.Dequeue()(parameters));
    }

    public IMessageServiceWithRawResponse WithRawResponse => throw new NotSupportedException();

    public IBatchService Batches => throw new NotSupportedException();

    public IMessageService WithOptions(Func<ClientOptions, ClientOptions> modifier) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<RawMessageStreamEvent> CreateStreaming(
        MessageCreateParams parameters, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<MessageTokensCount> CountTokens(
        MessageCountTokensParams parameters, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
