# Lecture Snippets — Claude Research Agent

Extracted from the `CsClaudeResearchAgent` repo for a live-coding lecture. Snippets are trimmed
(boilerplate, extra error handling, and logging removed where noted) but functionally accurate to
the real implementation — the untrimmed source is always at the file path given. Ordered as a
teaching sequence: setup → the pieces → the core loop → the result.

**Handoff note (for slide generation):** each section below is one slide/talking-point. Sections
marked **🟢 CHECKPOINT** are where you should actually run something live instead of just reading
code — see [Checkpoint summary](#checkpoint-summary) for the full list up front.

## Checkpoint summary

| # | What runs | Where in the sequence | What it proves |
|---|---|---|---|
| 1 | `dotnet run` with **no** `ANTHROPIC_API_KEY` set | After [4. Wiring it together](#4-wiring-it-together-the-composition-root) | Fails fast with a clear message instead of a confusing crash mid-conversation |
| 2 | `dotnet test` | After [9. The agent's own memory](#9-the-agents-own-memory-keeping-claude-honest) | The entire loop's logic (multi-turn tool calls, honesty checks, retries) is verified by 46 tests with **zero** network calls or API key |
| 3 | `dotnet run` with a real key, live question | [12. Running it for real](#12-running-it-for-real) | The full loop end-to-end: tool call happens, structured answer prints |

**Setup for checkpoints 1 & 3:** a terminal open to `src/ClaudeResearchAgent/`, and
`ANTHROPIC_API_KEY` unset (checkpoint 1) vs. exported (checkpoint 3). **Setup for checkpoint 2:**
none — works from a cold clone with no environment variables at all.

---

## 0. Orientation — project structure

**What it does:** shows where everything lives before diving into code, so live navigation makes
sense.

**File path:** whole repo (see also `CsClaudeApiClassDiagram.md` for the full class-level map).

```text
src/ClaudeResearchAgent/
├── Program.cs              # host bootstrap — wires DI, runs the console loop
├── Agent/                  # the agentic loop itself
│   ├── ResearchAgent.cs        — the loop: send → read → act → repeat
│   ├── ToolRegistry.cs         — safe dispatch to tools
│   ├── ExecutionTracker.cs     — "memory" of what actually happened
│   └── ResearchResponseParser.cs — turns free text into a typed, validated answer
├── Tools/                  # the things the agent can DO
│   ├── IAgentTool.cs            — the tool contract
│   ├── WikipediaTool.cs, WebSearchTool.cs, SaveTextTool.cs
├── Infrastructure/
│   └── ClaudeMessageAdapter.cs  — translates our types <-> Anthropic SDK types
└── ConsoleUi/
    └── ResearchConsoleSession.cs — the REPL
```

**Setup needed:** none — this is a slide, not a live step.

---

## 1. The tool contract

**What it does:** defines what a "tool" *is* — every capability the agent can invoke implements
this one interface.

**File path:** `src/ClaudeResearchAgent/Tools/IAgentTool.cs`

**Setup needed:** none — this is a pure interface, no dependencies.

```csharp
public interface IAgentTool
{
    string Name { get; }              // exact name Claude must use to call it
    string Description { get; }       // what Claude reads to decide when to use it
    JsonElement InputSchema { get; }  // JSON Schema describing the arguments

    Task<ToolExecutionResult> ExecuteAsync(
        JsonElement arguments, CancellationToken cancellationToken);
}
```

*Talking point:* this is the entire surface area an LLM needs to "see" a capability — a name, a
description in plain English, and a schema. Nothing here is Claude-specific.

---

## 2. A concrete tool

**What it does:** the simplest real tool — appends text to a file. Shows the shape every tool
follows: read arguments defensively, do the work, return a result that can never throw.

**File path:** `src/ClaudeResearchAgent/Tools/SaveTextTool.cs`

**Setup needed:** `IOptions<SaveTextToolOptions>` bound from config (shown in section 4). Trimmed
here: the real version also has a size limit, a semaphore for concurrent-write safety, and logging.

```csharp
public sealed class SaveTextTool(IOptions<SaveTextToolOptions> saveOptions) : IAgentTool
{
    public string Name => "save_text_to_file";

    public string Description =>
        "Appends the given research content to a local research notes file.";

    public JsonElement InputSchema => ToolSchemas.SingleRequiredStringProperty(
        "content", "The research content to append to the output file.");

    public async Task<ToolExecutionResult> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!ToolArguments.TryGetRequiredString(arguments, "content", out var content))
        {
            return ToolExecutionResult.Fail("Missing 'content' argument.", "invalid_argument");
        }

        var path = saveOptions.Value.OutputFilePath;
        await File.AppendAllTextAsync(path, content + Environment.NewLine, ct);

        return ToolExecutionResult.Ok($"Saved {content.Length} characters to {path}.");
    }
}
```

*Talking point:* notice there's no `path` argument in the schema — the model supplies content
only. It structurally cannot redirect a write anywhere else on disk.

---

## 3. Dispatching tool calls safely

**What it does:** the single choke point every tool call passes through — unknown tool names and
tool exceptions become a safe result instead of crashing the loop.

**File path:** `src/ClaudeResearchAgent/Agent/ToolRegistry.cs`

**Setup needed:** a `Dictionary<string, IAgentTool>` populated at construction (one entry per
registered tool — shown assembled in section 4). Trimmed here: observation-length truncation.

```csharp
public async Task<ToolExecutionResult> ExecuteAsync(
    string name, JsonElement arguments, CancellationToken cancellationToken)
{
    if (!_tools.TryGetValue(name, out var tool))
    {
        return ToolExecutionResult.Fail($"Tool '{name}' is not registered.", "unknown_tool");
    }

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(_toolTimeout);

    try
    {
        return await tool.ExecuteAsync(arguments, timeoutCts.Token);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return ToolExecutionResult.Fail($"Tool '{name}' failed: {ex.Message}", "tool_exception");
    }
}
```

*Talking point:* an LLM **will** eventually call a tool that doesn't exist, or hit one that throws.
This is the one place that has to handle both gracefully so the rest of the loop never has to think
about it.

---

## 4. Wiring it together (the composition root)

**What it does:** registers every tool as the same interface, so the loop can be handed "all the
tools" without knowing how many there are or what they do.

**File path:** `src/ClaudeResearchAgent/ServiceCollectionExtensions.cs`

**Setup needed:** standard .NET `IServiceCollection`/`IConfiguration` (from `Host.CreateApplicationBuilder`
in `Program.cs`). Trimmed here: HTTP client and web-search-provider registrations.

```csharp
public static IServiceCollection AddClaudeResearchAgent(
    this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
    services.AddSingleton(sp => sp.GetRequiredService<IOptions<AgentOptions>>().Value);

    services.AddAnthropicClient();

    services.AddSingleton<IAgentTool, WikipediaTool>();
    services.AddSingleton<IAgentTool, WebSearchTool>();
    services.AddSingleton<IAgentTool, SaveTextTool>();
    services.AddSingleton<ToolRegistry>();   // resolves IEnumerable<IAgentTool> — all 3 above

    services.AddSingleton<IResearchResponseParser, ResearchResponseParser>();
    services.AddSingleton<IResearchAgent, ResearchAgent>();

    return services;
}
```

*Talking point:* three `AddSingleton<IAgentTool, ...>()` calls, one shared interface — `ToolRegistry`
just asks DI for "every `IAgentTool`" and doesn't care how many there are. Adding tool #4 later is a
two-line change: implement `IAgentTool`, add one line here.

---

## 🟢 CHECKPOINT 1 — fail fast with no key

**Run this live** (from `src/ClaudeResearchAgent/`, with `ANTHROPIC_API_KEY` **unset**):

```bash
dotnet run
```

**Expected output:**

```text
Configuration is invalid:
  - Environment variable ANTHROPIC_API_KEY is not set. Set it before running (see .env.example); the key is never read from configuration files.
```
...and the process exits with code `1`.

**File path for the code behind this:** `src/ClaudeResearchAgent/Program.cs` (validation runs
*before* the DI container is even built — `src/ClaudeResearchAgent/Configuration/EnvironmentValidator.cs`
does the actual check).

```csharp
var validationErrors = EnvironmentValidator.Validate(agentOptions);
if (validationErrors.Count > 0)
{
    Console.Error.WriteLine("Configuration is invalid:");
    foreach (var error in validationErrors)
    {
        Console.Error.WriteLine($"  - {error}");
    }
    return 1;
}
```

*Talking point:* an agent that fails silently, or three layers deep with a confusing exception, is
a debugging nightmare. Fail at the front door, with a message a human can act on.

---

## 5. Telling the model how to plan (the system prompt)

**What it does:** the instructions that shape *how* Claude reasons about when to use tools and how
to format its final answer — this is the "planning" contract, entirely in plain English.

**File path:** `src/ClaudeResearchAgent/Agent/ResearchAgent.cs` (the `SystemPrompt` constant)

**Setup needed:** none — it's a string, sent as the `system` parameter on every request.

```csharp
private const string SystemPrompt = """
    You are a concise, careful research agent.

    - Use the available tools whenever you need current or verifiable information.
    - Never claim in your final answer that you used a tool unless you actually invoked it.
    - Never invent sources or URLs. Only cite a URL that a tool call actually returned to you.

    When, and only when, you are done calling tools, respond with ONLY a single JSON object:
    {
      "topic": "string",
      "summary": "string",
      "sources": [ { "title": "string", "url": "string", "excerpt": "string or null" } ],
      "toolsUsed": ["string", ...]
    }
    """;
```

*Talking point:* notice this prompt is doing double duty — steering *reasoning* ("don't invent
sources") and specifying an *output format*. Section 10 shows why we don't just trust the model to
follow the second part.

---

## 6. The agent loop itself (the core mechanism)

**What it does:** this *is* the agentic loop — send the conversation, look at what came back, and
either act (call tools, loop again) or finish (return the answer).

**File path:** `src/ClaudeResearchAgent/Agent/ResearchAgent.cs` (`ResearchAsync`, trimmed)

**Setup needed:** everything above this point — `IMessageService` (the Claude client), `ToolRegistry`,
`IResearchResponseParser`. Trimmed here: `CreateMessageAsync` (which builds the actual
`MessageCreateParams` and calls `messageService.Create`) is collapsed to one line — see the real
file for the full request-building and error-mapping code. Also trimmed: refusal handling,
timeouts, the format-repair retry path, and cancellation — all real, all omitted for slide space.

```csharp
for (var iteration = 1; iteration <= options.MaxIterations; iteration++)
{
    var response = await CreateMessageAsync(messages, toolDefinitions, ct);

    messages.Add(ClaudeMessageAdapter.BuildAssistantMessage(response));

    var toolUseBlocks = ClaudeMessageAdapter.ExtractToolUseBlocks(response);
    if (toolUseBlocks.Count > 0)
    {
        var results = await ExecuteToolCallsAsync(toolUseBlocks, tracker, ct);
        messages.Add(ClaudeMessageAdapter.BuildToolResultsMessage(results));
        continue;   // <-- loop again so Claude can see the tool results
    }

    var finalText = ClaudeMessageAdapter.ExtractFinalText(response);
    var parseResult = responseParser.Parse(finalText!);
    if (parseResult.Succeeded)
    {
        return Reconcile(parseResult.Response!, tracker);
    }

    // ...otherwise: ask Claude to fix its JSON, once, then loop again
}
```

*Talking point:* this is the whole "agentic" part in ~20 lines: **request → inspect → act-or-finish
→ repeat, bounded by `MaxIterations`.** Everything else in the codebase exists to make one of these
four steps safe, honest, or type-checked.

---

## 7. Reading the model's response by *type*, not position

**What it does:** a response can contain thinking, text, and tool-call blocks in any order — you
must select by type, never assume "the first block is the answer."

**File path:** `src/ClaudeResearchAgent/Infrastructure/ClaudeMessageAdapter.cs`

**Setup needed:** an Anthropic SDK `Message` (the raw API response) — nothing else.

```csharp
public static IReadOnlyList<ToolUseBlock> ExtractToolUseBlocks(Message response)
{
    var toolUses = new List<ToolUseBlock>();
    foreach (var block in response.Content)
    {
        if (block.TryPickToolUse(out var toolUse))
        {
            toolUses.Add(toolUse);
        }
    }
    return toolUses;
}

public static string? ExtractFinalText(Message response)
{
    foreach (var block in response.Content)
    {
        if (block.TryPickText(out var text)) return text.Text;
    }
    return null;
}
```

*Talking point:* a common bug in hand-rolled agent loops is `response.Content[0]` — this breaks the
moment the model leads with a reasoning ("thinking") block. Scan by type instead.

---

## 8. Carrying state across turns

**What it does:** the model's own previous turn — including tool calls it just made — has to be
echoed back verbatim before you can answer it. This is the loop's short-term "memory" of the
conversation itself.

**File path:** `src/ClaudeResearchAgent/Infrastructure/ClaudeMessageAdapter.cs` (`BuildAssistantMessage`, trimmed)

**Setup needed:** none beyond the SDK types already in scope. Trimmed here: the branch that
preserves "thinking" blocks (required by the API, omitted for space).

```csharp
public static MessageParam BuildAssistantMessage(Message response)
{
    var blocks = new List<ContentBlockParam>();

    foreach (var block in response.Content)
    {
        if (block.TryPickText(out var text))
        {
            blocks.Add(new TextBlockParam(text.Text));
        }
        else if (block.TryPickToolUse(out var toolUse))
        {
            blocks.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
        }
    }

    return new MessageParam { Role = Role.Assistant, Content = blocks };
}
```

*Talking point:* skip or reorder a block here and the *next* API call fails outright — the
conversation state has to match exactly what the model actually produced.

---

## 9. The agent's own memory: keeping Claude honest

**What it does:** the application's independent record of which tools *actually* ran and which
sources they *actually* returned — used to overrule anything Claude merely *claims* in its final
answer.

**File path:** `src/ClaudeResearchAgent/Agent/ExecutionTracker.cs` (shown almost in full — it's
already minimal)

**Setup needed:** none — plain in-memory state, one instance per research question.

```csharp
public sealed class ExecutionTracker
{
    private readonly List<string> _executedToolNames = [];
    private readonly Dictionary<string, ResearchSource> _retrievedSources = new();

    public void RecordSuccessfulExecution(string toolName, IReadOnlyList<ResearchSource> sources)
    {
        _executedToolNames.Add(toolName);
        foreach (var source in sources)
        {
            _retrievedSources[source.Url] = source;
        }
    }

    public IReadOnlyList<string> ExecutedToolNames => _executedToolNames.Distinct().ToList();

    public IReadOnlyList<ResearchSource> ReconcileSources(IReadOnlyList<ResearchSource> claimed) =>
        claimed.Where(s => _retrievedSources.ContainsKey(s.Url)).ToList();
}
```

*Talking point:* this is the answer to "how do you stop an LLM from hallucinating a source URL in
its *final* answer, after tools already ran cleanly?" — you don't trust the model's self-report at
all; you keep your own ledger and filter against it (see section 6's `Reconcile` call).

---

## 🟢 CHECKPOINT 2 — prove the loop works without touching a real API

**Run this live** (works from a cold clone, no env vars needed):

```bash
dotnet test
```

**Expected output (tail of it):**

```text
Passed!  - Failed:     0, Passed:    46, Skipped:     0, Total:    46, Duration: 1 s - ClaudeResearchAgent.Tests.dll (net8.0)
```

**File path:** `tests/ClaudeResearchAgent.Tests/ResearchAgentTests.cs` is the one to open live —
scroll to `Executes_multiple_tool_requests_from_a_single_assistant_turn_with_correct_ids` or
`Strips_invented_sources_and_uses_recorded_tool_names_as_authoritative` as a "here's section 6 and
9 being tested" moment.

*Talking point:* every test fakes the Claude client (`FakeMessageService`, a hand-written
implementation of the SDK's own `IMessageService` interface) — proving the *loop's logic* is
correct doesn't require a network call, a key, or a dollar spent.

---

## 10. Turning free text into a typed, validated answer

**What it does:** the JSON contract from section 5 is a *request*, not a guarantee — this parses
and validates it before anything downstream can trust it.

**File path:** `src/ClaudeResearchAgent/Agent/ResearchResponseParser.cs` (trimmed — real version
also strips a stray ` ```json ` fence and logs the original exception)

**Setup needed:** a `ResearchResponse` record to deserialize into (shown below) and
`ResearchResponseValidation.Validate` (checks non-empty topic/summary, valid absolute URLs).

```csharp
public ResponseParseResult Parse(string rawText)
{
    ResearchResponse? response;
    try
    {
        response = JsonSerializer.Deserialize<ResearchResponse>(rawText, SerializerOptions);
    }
    catch (JsonException ex)
    {
        return ResponseParseResult.Failure($"Not valid JSON: {ex.Message}");
    }

    var validationErrors = ResearchResponseValidation.Validate(response!);
    if (validationErrors.Count > 0)
    {
        return ResponseParseResult.Failure(string.Join(" ", validationErrors));
    }

    return ResponseParseResult.Success(response!);
}
```

The type it deserializes into — `src/ClaudeResearchAgent/Models/ResearchResponse.cs`:

```csharp
public sealed record ResearchResponse
{
    public required string Topic { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<ResearchSource> Sources { get; init; }
    public required IReadOnlyList<string> ToolsUsed { get; init; }
}
```

*Talking point:* `required` members mean `System.Text.Json` itself rejects JSON missing a field —
free type-safety before your own validation even runs. Section 6 shows the caller retrying once on
failure, then giving up — never an infinite repair loop.

---

## 11. Tying it to a console loop

**What it does:** the outermost REPL — read a question, run the whole loop from section 6, print
the result, repeat.

**File path:** `src/ClaudeResearchAgent/ConsoleUi/ResearchConsoleSession.cs` (`RunAsync`, trimmed —
real version handles cancellation and prints a friendly message on failure instead of just looping)

**Setup needed:** an `IResearchAgent` (the fully-wired object graph from section 4).

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    Console.Write("\nResearch question> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input is "exit" or "quit")
    {
        break;
    }

    var response = await agent.ResearchAsync(input, cancellationToken);
    PrintResponse(response);
}
```

---

## 12. Running it for real

**🟢 CHECKPOINT 3 — the payoff.** Run live (from `src/ClaudeResearchAgent/`, with a real
`ANTHROPIC_API_KEY` exported — **don't show the key on screen**):

```bash
export ANTHROPIC_API_KEY="sk-ant-...(pre-set out of frame, or paste fast)..."
dotnet run
```

**Suggested live question** — pick one that needs a real tool call, so the audience sees section 6's
loop actually iterate:

```text
Research question> What is the Wikipedia summary for hammerhead sharks, and what's its canonical URL?
```

**Expected shape of output** (progress line, then the final structured answer):

```text
info: ClaudeResearchAgent.Agent.ResearchAgent[0]
      Invoking tool 'wikipedia'...
info: ClaudeResearchAgent.Agent.ResearchAgent[0]
      Tool 'wikipedia' succeeded.

Topic:
Hammerhead Sharks

Summary:
...

Sources:
1. Hammerhead shark
   https://en.wikipedia.org/wiki/Hammerhead_shark

Tools used:
wikipedia
```

*Talking point:* trace it back through every section: the model chose to call `wikipedia` (5), the
loop executed it safely (3) and looped again (6), the block-reading code found the tool call (7),
the tracker recorded a real source (9), and the final JSON only kept the source that ledger
actually confirms (9 + 10). Nothing on screen was hand-waved — it's the same 46-line loop from
section 6, running against the real API this time.
