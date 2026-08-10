using System.Text.Json;
using ClaudeResearchAgent.Models;
using ClaudeResearchAgent.Tools;
using Microsoft.Extensions.Logging;

namespace ClaudeResearchAgent.Agent;

/// <summary>
/// The single dispatch point between Claude's tool_use requests and the registered
/// <see cref="IAgentTool"/> implementations. Owns the cross-cutting behavior every tool call needs
/// — unknown-tool safety, per-call timeouts, and bounding observation size — so individual tools
/// don't have to reimplement it.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.Ordinal);
    private readonly TimeSpan _toolTimeout;
    private readonly int _maxObservationCharacters;
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(IEnumerable<IAgentTool> tools, AgentOptions agentOptions, ILogger<ToolRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(agentOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _toolTimeout = TimeSpan.FromSeconds(agentOptions.ToolTimeoutSeconds);
        _maxObservationCharacters = agentOptions.MaximumToolResultCharacters;
        _logger = logger;

        foreach (var tool in tools)
        {
            Register(tool);
        }
    }

    public IReadOnlyCollection<IAgentTool> RegisteredTools => _tools.Values;

    /// <summary>Adds a tool by its exact name. Throws if that name is already taken.</summary>
    public void Register(IAgentTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (!_tools.TryAdd(tool.Name, tool))
        {
            throw new InvalidOperationException($"A tool named '{tool.Name}' is already registered.");
        }
    }

    public bool TryGet(string name, out IAgentTool? tool) => _tools.TryGetValue(name, out tool);

    /// <summary>
    /// Executes a tool by name, always returning a <see cref="ToolExecutionResult"/> — never
    /// throwing — so the agent loop can hand the outcome straight back to Claude as a tool_result.
    /// </summary>
    public async Task<ToolExecutionResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(name, out var tool))
        {
            _logger.LogWarning("Claude requested unregistered tool '{ToolName}'.", name);
            return ToolExecutionResult.Fail(
                $"Tool '{name}' is not registered. Available tools: {string.Join(", ", _tools.Keys)}.",
                "unknown_tool");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_toolTimeout);

        try
        {
            var result = await tool.ExecuteAsync(arguments, timeoutCts.Token).ConfigureAwait(false);
            return Bound(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Tool '{ToolName}' timed out after {TimeoutSeconds}s.", name, _toolTimeout.TotalSeconds);
            return ToolExecutionResult.Fail($"Tool '{name}' timed out after {_toolTimeout.TotalSeconds:0}s.", "timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Tool '{ToolName}' threw an unhandled exception.", name);
            return ToolExecutionResult.Fail($"Tool '{name}' failed unexpectedly: {ex.Message}", "tool_exception");
        }
    }

    private ToolExecutionResult Bound(ToolExecutionResult result)
    {
        if (result.Observation.Length <= _maxObservationCharacters)
        {
            return result;
        }

        _logger.LogInformation(
            "Truncating tool observation from {Original} to {Max} characters.",
            result.Observation.Length,
            _maxObservationCharacters);

        var truncated = result.Observation[.._maxObservationCharacters] + "... [truncated]";
        return result with { Observation = truncated };
    }
}
