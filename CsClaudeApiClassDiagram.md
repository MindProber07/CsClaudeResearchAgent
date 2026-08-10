# Claude Research Agent — Class Diagrams

Companion to [README.md](README.md), which covers the runtime architecture, sequence flow, and
retry behavior. This document covers static structure: the actual classes, interfaces, and
relationships in `src/ClaudeResearchAgent/`.

Diagrams are split by layer/namespace so each one stays legible — start with
[System Overview](#0-system-overview) for how they fit together, then drill into the layer you
care about.

> **Reading notes:** async methods are shown with their unwrapped result type (the `Task<...>` /
> `Task` wrapper is dropped for readability). `<<static>>` marks a static utility class; `<<record>>`
> marks a C# `record`. Interface realization is drawn `Interface <|.. Implementation`.

## Contents

- [0. System overview](#0-system-overview)
- [1. Domain models (`Models`)](#1-domain-models-models)
- [2. Tools and search (`Tools`, `Search`)](#2-tools-and-search-tools-search)
- [3. Agent orchestration (`Agent`)](#3-agent-orchestration-agent)
- [4. Infrastructure, configuration, and console (`Infrastructure`, `Configuration`, `ConsoleUi`)](#4-infrastructure-configuration-and-console-infrastructure-configuration-consoleui)

## 0. System overview

High-level relationships only — no members. See the sections below for full class detail, and the
README's [architecture diagram](README.md#architecture-overview) for how this maps onto runtime
data flow.

```mermaid
classDiagram
    class Program
    class ServiceCollectionExtensions
    class ResearchConsoleSession
    class IResearchAgent
    class ResearchAgent
    class ToolRegistry
    class ExecutionTracker
    class IResearchResponseParser
    class ResearchResponseParser
    class IAgentTool
    class WikipediaTool
    class WebSearchTool
    class SaveTextTool
    class IWebSearchProvider
    class DuckDuckGoSearchProvider
    class ClaudeMessageAdapter
    class AnthropicClientFactory
    class HttpClientRegistration
    class TransientRetryHandler
    class ResearchResponse
    class AgentOptions

    Program --> ServiceCollectionExtensions : composes DI container via
    Program --> ResearchConsoleSession : runs
    ResearchConsoleSession --> IResearchAgent : calls ResearchAsync
    IResearchAgent <|.. ResearchAgent
    ResearchAgent --> ToolRegistry
    ResearchAgent --> ExecutionTracker
    ResearchAgent --> IResearchResponseParser
    ResearchAgent --> ClaudeMessageAdapter : uses
    ResearchAgent --> AgentOptions
    ResearchAgent --> ResearchResponse : produces
    IResearchResponseParser <|.. ResearchResponseParser
    ToolRegistry --> IAgentTool : dispatches to
    IAgentTool <|.. WikipediaTool
    IAgentTool <|.. WebSearchTool
    IAgentTool <|.. SaveTextTool
    WebSearchTool --> IWebSearchProvider
    IWebSearchProvider <|.. DuckDuckGoSearchProvider
    WikipediaTool --> TransientRetryHandler : HTTP pipeline
    DuckDuckGoSearchProvider --> TransientRetryHandler : HTTP pipeline
    ServiceCollectionExtensions --> HttpClientRegistration : calls
    ServiceCollectionExtensions --> AnthropicClientFactory : calls
```

## 1. Domain models (`Models`)

Framework-agnostic records that carry data between every other layer. Nothing here references the
Anthropic SDK or `System.Net.Http` — see [ADR note in README](README.md#architecture-overview) on
why that boundary is kept clean.

```mermaid
classDiagram
    class ResearchResponse {
        <<record>>
        +string Topic
        +string Summary
        +IReadOnlyList~ResearchSource~ Sources
        +IReadOnlyList~string~ ToolsUsed
    }

    class ResearchResponseValidation {
        <<static>>
        +Validate(ResearchResponse response) IReadOnlyList~string~
    }

    class ResearchSource {
        <<record>>
        +string Title
        +string Url
        +string? Excerpt
    }

    class ResearchSourceValidation {
        <<static>>
        +IsValid(ResearchSource source) bool
    }

    class SearchResult {
        <<record>>
        +string Title
        +string Url
        +string Snippet
    }

    class ToolExecutionResult {
        <<record>>
        +bool Success
        +string Observation
        +string? ErrorCategory
        +IReadOnlyList~ResearchSource~ Sources
        +Ok(string observation, IReadOnlyList~ResearchSource~ sources) ToolExecutionResult
        +Fail(string observation, string errorCategory) ToolExecutionResult
    }

    class WikipediaLookupResult {
        <<record>>
        +bool Success
        +string? Title
        +string? Url
        +string? Summary
        +string? ErrorCategory
    }

    ResearchResponse "1" o-- "0..*" ResearchSource : Sources
    ToolExecutionResult "1" o-- "0..*" ResearchSource : Sources
    ResearchResponseValidation ..> ResearchResponse : validates
    ResearchResponseValidation ..> ResearchSourceValidation : uses
    ResearchSourceValidation ..> ResearchSource : validates
```

*(`WikipediaLookupResult` is serialized to JSON as a `WikipediaTool` observation — it does not
compose with the other models directly; see [section 2](#2-tools-and-search-tools-search).)*

## 2. Tools and search (`Tools`, `Search`)

The three agent-callable capabilities, all implementing `IAgentTool`, plus the `search` tool's
provider abstraction.

```mermaid
classDiagram
    class IAgentTool {
        <<interface>>
        +string Name
        +string Description
        +JsonElement InputSchema
        +ExecuteAsync(JsonElement arguments, CancellationToken ct) ToolExecutionResult
    }

    class WikipediaTool {
        -IHttpClientFactory httpClientFactory
        -ILogger~WikipediaTool~ logger
        +string Name
        +string Description
        +JsonElement InputSchema
        +ExecuteAsync(JsonElement arguments, CancellationToken ct) ToolExecutionResult
    }

    class WebSearchTool {
        -IWebSearchProvider searchProvider
        -ILogger~WebSearchTool~ logger
        +string Name
        +string Description
        +JsonElement InputSchema
        +ExecuteAsync(JsonElement arguments, CancellationToken ct) ToolExecutionResult
    }

    class SaveTextTool {
        -IOptions~SaveTextToolOptions~ saveOptions
        -IOptions~AgentOptions~ agentOptions
        -ILogger~SaveTextTool~ logger
        +string Name
        +string Description
        +JsonElement InputSchema
        +ExecuteAsync(JsonElement arguments, CancellationToken ct) ToolExecutionResult
    }

    class IWebSearchProvider {
        <<interface>>
        +SearchAsync(string query, CancellationToken ct) IReadOnlyList~SearchResult~
    }

    class DuckDuckGoSearchProvider {
        -IHttpClientFactory httpClientFactory
        -WebSearchToolOptions options
        -ILogger~DuckDuckGoSearchProvider~ logger
        +SearchAsync(string query, CancellationToken ct) IReadOnlyList~SearchResult~
    }

    class ToolArguments {
        <<static>>
        <<internal>>
        +TryGetRequiredString(JsonElement arguments, string propertyName, out string value) bool
    }

    class ToolSchemas {
        <<static>>
        <<internal>>
        +SingleRequiredStringProperty(string propertyName, string description) JsonElement
    }

    class WikipediaToolOptions {
        +string UserAgent
        +int RequestTimeoutSeconds
        +int MaxRetryAttempts
    }

    class WebSearchToolOptions {
        +int MaxResults
        +int MaxSnippetCharacters
        +int RequestTimeoutSeconds
        +int MaxRetryAttempts
    }

    class SaveTextToolOptions {
        +string OutputFilePath
    }

    IAgentTool <|.. WikipediaTool
    IAgentTool <|.. WebSearchTool
    IAgentTool <|.. SaveTextTool
    IWebSearchProvider <|.. DuckDuckGoSearchProvider

    WebSearchTool --> IWebSearchProvider : delegates to
    WikipediaTool ..> ToolArguments : uses
    WikipediaTool ..> ToolSchemas : uses
    WebSearchTool ..> ToolArguments : uses
    WebSearchTool ..> ToolSchemas : uses
    SaveTextTool ..> ToolArguments : uses
    SaveTextTool ..> ToolSchemas : uses

    WikipediaTool ..> WikipediaToolOptions : bound from Tools.Wikipedia
    DuckDuckGoSearchProvider ..> WebSearchToolOptions : bound from Tools.Search
    SaveTextTool ..> SaveTextToolOptions : bound from Tools.SaveText
```

## 3. Agent orchestration (`Agent`)

The explicit tool-calling loop and everything it depends on: dispatch, structured-output parsing,
and the application-recorded state that keeps Claude's final answer honest.

```mermaid
classDiagram
    class IResearchAgent {
        <<interface>>
        +ResearchAsync(string question, CancellationToken ct) ResearchResponse
    }

    class ResearchAgent {
        -IMessageService messageService
        -ToolRegistry toolRegistry
        -IResearchResponseParser responseParser
        -AgentOptions options
        -ILogger~ResearchAgent~ logger
        -SystemPrompt string
        +ResearchAsync(string question, CancellationToken ct) ResearchResponse
        -CreateMessageAsync(...) Message
        -ExecuteToolCallsAsync(...) List
        -Reconcile(ResearchResponse response, ExecutionTracker tracker) ResearchResponse
        -BuildRepairPrompt(string failureReason) string
    }

    class ToolRegistry {
        -Dictionary~string, IAgentTool~ tools
        -TimeSpan toolTimeout
        -int maxObservationCharacters
        -ILogger~ToolRegistry~ logger
        +IReadOnlyCollection~IAgentTool~ RegisteredTools
        +Register(IAgentTool tool)
        +TryGet(string name, out IAgentTool tool) bool
        +ExecuteAsync(string name, JsonElement arguments, CancellationToken ct) ToolExecutionResult
        -Bound(ToolExecutionResult result) ToolExecutionResult
    }

    class ExecutionTracker {
        -List~string~ executedToolNames
        -Dictionary~string, ResearchSource~ retrievedSources
        +IReadOnlyList~string~ ExecutedToolNames
        +RecordSuccessfulExecution(string toolName, IReadOnlyList~ResearchSource~ sources)
        +ReconcileSources(IReadOnlyList~ResearchSource~ claimedSources) IReadOnlyList~ResearchSource~
    }

    class IResearchResponseParser {
        <<interface>>
        +Parse(string rawText) ResponseParseResult
    }

    class ResearchResponseParser {
        -JsonSerializerOptions SerializerOptions
        -ILogger~ResearchResponseParser~ logger
        +Parse(string rawText) ResponseParseResult
        -ExtractJsonPayload(string rawText) string
    }

    class ResponseParseResult {
        <<record>>
        +ResearchResponse? Response
        +string? FailureReason
        +bool Succeeded
        +Success(ResearchResponse response) ResponseParseResult
        +Failure(string reason) ResponseParseResult
    }

    class AgentOptions {
        +string SectionName
        +string Model
        +int MaxIterations
        +int OverallTimeoutSeconds
        +int ToolTimeoutSeconds
        +int MaximumToolResultCharacters
        +int MaximumSaveCharacters
        +int MaximumFormatRepairAttempts
        +int MaxTokens
        +Validate() IReadOnlyList~string~
    }

    class AgentExecutionException {
        <<exception>>
        +AgentFailureReason Reason
    }

    class AgentFailureReason {
        <<enumeration>>
        MaxIterationsExceeded
        OverallTimeout
        NoUsableResponse
        InvalidStructuredOutput
        Refusal
        ApiError
        MissingApiKey
        ModelUnavailable
    }

    IResearchAgent <|.. ResearchAgent
    IResearchResponseParser <|.. ResearchResponseParser
    AgentExecutionException --> AgentFailureReason : Reason

    ResearchAgent --> ToolRegistry : dispatches tool_use blocks via
    ResearchAgent --> ExecutionTracker : records outcomes in
    ResearchAgent --> IResearchResponseParser : parses final answer with
    ResearchAgent --> AgentOptions : bounded by
    ResearchAgent ..> AgentExecutionException : throws
    ResearchAgent ..> ResearchResponse : returns

    ToolRegistry --> AgentOptions : reads timeout/size limits from
    ToolRegistry o-- IAgentTool : owns 0..*

    ResearchResponseParser ..> ResponseParseResult : returns
    ResearchResponseParser ..> ResearchResponseValidation : validates via
    ExecutionTracker o-- ResearchSource : retrievedSources
```

## 4. Infrastructure, configuration, and console (`Infrastructure`, `Configuration`, `ConsoleUi`)

The provider-specific boundary (`Infrastructure` is the only layer besides `Agent` allowed to
reference Anthropic SDK types — see [README](README.md#architecture-overview)), plus startup
validation and the REPL loop.

```mermaid
classDiagram
    class ClaudeMessageAdapter {
        <<static>>
        +BuildToolDefinition(IAgentTool tool) ToolUnion
        +BuildUserTextMessage(string text) MessageParam
        +BuildAssistantMessage(Message response) MessageParam
        +BuildToolResultsMessage(results) MessageParam
        +ExtractToolUseBlocks(Message response) IReadOnlyList~ToolUseBlock~
        +ExtractFinalText(Message response) string?
        -ConvertSchema(JsonElement schemaJson) InputSchema
    }

    class AnthropicClientFactory {
        <<static>>
        +AddAnthropicClient(IServiceCollection services) IServiceCollection
    }

    class HttpClientRegistration {
        <<static>>
        +string WikipediaClientName
        +string DuckDuckGoClientName
        +AddClaudeResearchAgentHttpClients(IServiceCollection services) IServiceCollection
    }

    class TransientRetryHandler {
        -ResiliencePipeline~HttpResponseMessage~ pipeline
        +TransientRetryHandler(int maxRetryAttempts, ILogger logger)
        #SendAsync(HttpRequestMessage request, CancellationToken ct) HttpResponseMessage
    }

    class EnvironmentValidator {
        <<static>>
        +string ApiKeyEnvironmentVariable
        +Validate(AgentOptions agentOptions) IReadOnlyList~string~
    }

    class DotEnvLoader {
        <<static>>
        +LoadIfPresent(string path)
    }

    class ResearchConsoleSession {
        -IResearchAgent agent
        -ILogger~ResearchConsoleSession~ logger
        -string[] ExitCommands
        +RunAsync(CancellationToken ct) int
        -PrintBanner()
        -PrintResponse(ResearchResponse response)
    }

    class ServiceCollectionExtensions {
        <<static>>
        +AddClaudeResearchAgent(IServiceCollection services, IConfiguration configuration) IServiceCollection
    }

    class DelegatingHandler {
        <<framework>>
    }

    TransientRetryHandler --|> DelegatingHandler

    ServiceCollectionExtensions --> HttpClientRegistration : calls
    ServiceCollectionExtensions --> AnthropicClientFactory : calls
    ServiceCollectionExtensions ..> EnvironmentValidator : (validated earlier, in Program.cs)

    HttpClientRegistration --> TransientRetryHandler : attaches as message handler

    ResearchConsoleSession --> IResearchAgent : calls ResearchAsync
    ResearchConsoleSession ..> AgentExecutionException : catches

    ClaudeMessageAdapter ..> IAgentTool : reads InputSchema from
    ClaudeMessageAdapter ..> ToolExecutionResult : reads Observation from
```

---

For how these classes actually get exercised at runtime — the iteration loop, retry timing, and
what gets sent back to Claude on each turn — see the [sequence](README.md#the-agent-loop) and
[retry](README.md#tool-failure-and-retry-behavior) diagrams in the README.
