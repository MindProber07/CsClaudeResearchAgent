using ClaudeResearchAgent.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClaudeResearchAgent.Tests;

public class ResearchResponseParsingTests
{
    private readonly ResearchResponseParser _parser = new(NullLogger<ResearchResponseParser>.Instance);

    [Fact]
    public void Parses_valid_json_into_a_validated_response()
    {
        const string json = """
            {
              "topic": "Hammerhead Sharks",
              "summary": "A family of sharks named for their distinctive head shape.",
              "sources": [
                { "title": "Hammerhead shark", "url": "https://en.wikipedia.org/wiki/Hammerhead_shark", "excerpt": "..." }
              ],
              "toolsUsed": ["wikipedia"]
            }
            """;

        var result = _parser.Parse(json);

        Assert.True(result.Succeeded);
        Assert.Equal("Hammerhead Sharks", result.Response!.Topic);
        Assert.Single(result.Response.Sources);
    }

    [Fact]
    public void Strips_a_markdown_json_fence_before_parsing()
    {
        const string fenced = """
            ```json
            { "topic": "T", "summary": "S", "sources": [], "toolsUsed": [] }
            ```
            """;

        var result = _parser.Parse(fenced);

        Assert.True(result.Succeeded);
        Assert.Equal("T", result.Response!.Topic);
    }

    [Fact]
    public void Rejects_malformed_json()
    {
        var result = _parser.Parse("{ this is not json");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Rejects_json_missing_required_fields()
    {
        var result = _parser.Parse("""{ "topic": "T" }""");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Rejects_an_empty_topic()
    {
        var result = _parser.Parse("""{ "topic": "", "summary": "S", "sources": [], "toolsUsed": [] }""");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Rejects_a_source_with_a_non_absolute_url()
    {
        const string json = """
            {
              "topic": "T",
              "summary": "S",
              "sources": [ { "title": "Bad", "url": "not-a-url" } ],
              "toolsUsed": []
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Rejects_a_source_with_a_non_http_scheme()
    {
        const string json = """
            {
              "topic": "T",
              "summary": "S",
              "sources": [ { "title": "Bad", "url": "ftp://example.test/file" } ],
              "toolsUsed": []
            }
            """;

        var result = _parser.Parse(json);

        Assert.False(result.Succeeded);
    }
}
