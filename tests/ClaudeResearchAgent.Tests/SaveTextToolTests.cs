using System.Text.Json;
using ClaudeResearchAgent.Agent;
using ClaudeResearchAgent.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ClaudeResearchAgent.Tests;

public sealed class SaveTextToolTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _outputPath;

    public SaveTextToolTests()
    {
        _tempDirectory = Directory.CreateTempSubdirectory("claude-research-agent-tests-").FullName;
        _outputPath = Path.Combine(_tempDirectory, "research_output.txt");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private SaveTextTool CreateTool(int maximumSaveCharacters = 50_000) => new(
        Options.Create(new SaveTextToolOptions { OutputFilePath = _outputPath }),
        Options.Create(new AgentOptions { Model = "claude-sonnet-5", MaximumSaveCharacters = maximumSaveCharacters }),
        NullLogger<SaveTextTool>.Instance);

    private static JsonElement Content(string content, string? extraPath = null) =>
        extraPath is null
            ? JsonSerializer.SerializeToElement(new { content })
            : JsonSerializer.SerializeToElement(new { content, path = extraPath });

    [Fact]
    public async Task Creates_the_output_file_when_it_does_not_exist()
    {
        Assert.False(File.Exists(_outputPath));
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(Content("first finding"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(_outputPath));
        Assert.Contains("first finding", await File.ReadAllTextAsync(_outputPath));
    }

    [Fact]
    public async Task Appends_without_overwriting_previous_entries()
    {
        var tool = CreateTool();

        await tool.ExecuteAsync(Content("first finding"), CancellationToken.None);
        await tool.ExecuteAsync(Content("second finding"), CancellationToken.None);

        var contents = await File.ReadAllTextAsync(_outputPath);
        Assert.Contains("first finding", contents, StringComparison.Ordinal);
        Assert.Contains("second finding", contents, StringComparison.Ordinal);
        Assert.True(contents.IndexOf("first finding", StringComparison.Ordinal) <
                    contents.IndexOf("second finding", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejects_content_over_the_configured_limit()
    {
        var tool = CreateTool(maximumSaveCharacters: 10);

        var result = await tool.ExecuteAsync(Content(new string('a', 11)), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("content_too_large", result.ErrorCategory);
        Assert.False(File.Exists(_outputPath));
    }

    [Fact]
    public async Task Ignores_any_path_argument_and_always_writes_to_the_configured_file()
    {
        var maliciousPath = Path.Combine(_tempDirectory, "should-not-be-used.txt");
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(Content("content", extraPath: maliciousPath), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(_outputPath));
        Assert.False(File.Exists(maliciousPath));
    }

    [Fact]
    public async Task Serializes_concurrent_writes_without_corrupting_the_file()
    {
        var tool = CreateTool();
        var markers = Enumerable.Range(0, 25).Select(i => $"concurrent-entry-{i}").ToList();

        await Task.WhenAll(markers.Select(marker => tool.ExecuteAsync(Content(marker), CancellationToken.None)));

        var contents = await File.ReadAllTextAsync(_outputPath);
        foreach (var marker in markers)
        {
            Assert.Contains(marker, contents, StringComparison.Ordinal);
        }
    }
}
