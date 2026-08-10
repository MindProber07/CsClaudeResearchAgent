namespace ClaudeResearchAgent.Tests.TestSupport;

/// <summary>Always hands back the one <see cref="HttpClient"/> it was built with, regardless of
/// the requested name — enough for tests that only ever need a single named client.</summary>
internal sealed class SingleClientHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
