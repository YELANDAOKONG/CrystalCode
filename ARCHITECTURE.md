# Architecture

## Status

This document is the authoritative architecture for the current coding
product. Public names may change while the product has no compatibility
baseline.

## Dependency direction

```text
CrystalCode   (terminal host: the executable)
    ↓                    ↓
CrystalCode.Display      CrystalCode.Engine
    ↓                        ↓
Spectre.Console              CrystalCode.Providers
(rasterization;                  ↓
Terminal.Gui is              Crystal
referenced, not called)

CrystalCode.Engine also references CrystalCode.Tools, CrystalCode.Plugins,
Crystal.Tools, Crystal.Agents, and Crystal.Harness directly.
CrystalCode.Tools and CrystalCode.Plugins each reference only Crystal and
Crystal.Tools. CrystalCode references
Spectre.Console and Spectre.Console.Cli for its commands and cards.
CrystalCode.Display references Spectre.Console and Terminal.Gui only.
It does not reference Crystal, Crystal.Tools, CrystalCode.Tools, the
engine, or the executable host. CrystalCode.Engine does not reference
Spectre.Console, Terminal.Gui, CrystalCode.Display, or the executable
host, and it never touches the console. CrystalCode.Providers
references only Crystal. No project in this repository modifies Crystal.
```

A second front end (a desktop application, for example) references
CrystalCode.Engine and supplies its own surface. It does not copy engine code.
`CrystalCode.Server` sits beside `CrystalCode.Display` under the executable:
it references CrystalCode.Engine and the `Microsoft.AspNetCore.App` shared
framework only. It does not reference CrystalCode.Display, Spectre.Console,
Terminal.Gui, the executable, or `System.Console`. The executable references
it. `CrystalCode.Server.Tests` references CrystalCode.Server and guards that
direction. See [docs/server.md](docs/server.md).
`CrystalCode.Bridge` sits beside them: it references CrystalCode.Engine only
and no package. It does not reference CrystalCode.Display,
CrystalCode.Server, Spectre.Console, Terminal.Gui, the executable, or
`System.Console`. The executable references it.
`CrystalCode.Bridge.Tests` references CrystalCode.Bridge and guards that
direction. See [docs/bridge.md](docs/bridge.md).
If that desktop front end is built with Avalonia, write the UI in C# markup.
Do not add XAML or AXAML files.
`EngineAssemblyTests` and `DisplayAssemblyTests` fail the build when either
direction above is crossed.

`CrystalCode.Platform` is deferred. The project does not exist, it is not
in the graph above, and it is not a Crystal project. Do not add it, reference
it, or reserve empty public types until a new decision defines its behavior
and ownership. Its intended job is a platform abstraction so
`CrystalCode.Engine` does not own operating-system-specific process and
clipboard details. Whether `CrystalCode.Display` would reference that
abstraction is not decided. Until then, each assembly keeps the platform
branches it already has: clipboard reading, path comparison, file modes, and
process launch stay in `CrystalCode.Engine`; console and composer key
differences stay in `CrystalCode.Display`; version text and launch path
comparison stay in `CrystalCode`.

CrystalCode.Tests references CrystalCode.
CrystalCode.Engine.Tests references CrystalCode.Engine.
CrystalCode.Display.Tests references CrystalCode.Display.
CrystalCode.Providers.Tests references CrystalCode.Providers.

## Assembly ownership

### CrystalCode

The terminal host and the only executable. Owns CLI commands, the terminal
host loop (alternate screen, key loop, Ctrl+C, termination signals), the headless `crystal run`
entry, and the projection of engine events onto CrystalCode.Display. It also
owns the terminal surfaces for the engine's prompts: approval cards and keys,
the question overlay, and the saved-session picker. `crystal run` supplies
its own observer, approval prompt, question prompt, and session chooser. It
does not own session logic, approval policy, prompts, storage, or tools, and
it does not own the frame painter, composer buffer, or transcript log.
`crystal version` and `crystal --version` print the build identity as plain
text and exit. The build section is `Crystal Code` (this repository's
commit), `Crystal` (the Crystal library repository's commit), `SDK` (the
.NET SDK that compiled the executable), and `Configuration` (the MSBuild
configuration stamped on the executable). A blank line separates it from
the host section: `Runtime` (the shared framework hosting the process) and
`OS` (`RuntimeInformation.OSDescription` plus the operating-system
architecture). A line is omitted when that value was not recorded. An empty
section is omitted, including the blank line. Commits are the 40-character
source revision the SDK appends to the assembly informational version. The
assembly version placeholder is left unread. `SDK` is the
`NETCoreSdkVersion` assembly metadata stamped by `Directory.Build.targets`
for projects in this repository. `Configuration` is
`AssemblyConfigurationAttribute`. The host passes
`RuntimeInformation.FrameworkDescription`, `OSDescription`, and
`OSArchitecture`. The description is kept whole and the architecture is
appended in lower case. The report does not start a process. The outbound
`User-Agent` stays `Crystal Code`.

### CrystalCode.Tools

The contract external dotnet tools may reference when a call needs host
facts. It defines `ToolHostContext`, `IHostTool`,
`IHostMultimodalTool`, and the optional `IHostToolApprovalClassifier`.
`ToolHostContext` carries the workspace root, the session id, the approval mode,
and the extension's global and project runtime-data directories. An approval
classifier receives the model call and a context carrying the workspace root,
session id, and approval mode before invocation and returns the call's risk
and authority; the engine merges that proposal with its own classification and
it can only raise risk or authority, never lower them. `ClassifyApprovalAsync`
receives user/turn cancellation and an independent two-second timeout. Its
entry is dispatched on a worker so blocking before returning a task does not
block approval. After timeout or cancellation that classifier instance stays
disabled until the external catalog reloads; late work is observed and its
result is discarded. In-process code still cannot be forcibly terminated.
Null results, exceptions, timeouts, disabled instances, and either invalid
classification field fail closed to Privileged + PrivilegedEscalation, merged
with the host floor. User/turn cancellation propagates without approval.
Classification summaries are authored by the host; a tool's `Summary` is not
sent to the operator or the reviewer. The engine awaits `IApprovalClassifier`
`ClassifyAsync`; its default implementation adapts existing synchronous plugin
classifiers. Synchronous external classification fails closed when a tool has
an asynchronous pre-classifier. It references only
Crystal and Crystal.Tools. It does not reference the engine, the display, the
executable, or a terminal library. Tools that implement only Crystal's `ITool`
or `IMultimodalTool` do not reference it. There is no paired test project;
dispatch, approval classification, and load-context identity are tested in
CrystalCode.Engine.Tests.

### CrystalCode.Plugins

The contract a disk plugin references. It defines `IPlugin`, contribution
lists, the hook and raw hook interfaces, the hook context types, the model
and environment views, and `IPluginDataDirectory` for an extension's runtime
data directories. It references only Crystal and
Crystal.Tools. It does not reference the engine, the display, the
executable, or a terminal library. There is no paired test project;
discovery, the load context, and hook order are tested in
CrystalCode.Engine.Tests.

### CrystalCode.Engine

The front-end-neutral class library. Owns session and turn execution,
Plan, Work, and custom-mode catalogs, approval policy, context compaction, `~/.crystal`
storage, built-in coding tools, operator tool sets, operator plugins,
prompts, in-process plugin contracts, slash commands, and the event stream a front end observes.
It never reads a key, writes to the console, or paints. See
[Engine contract](#engine-contract).

### CrystalCode.Engine.Tests

Engine tests: workspace fencing, approval classification, compaction
selection, session serialization, command behavior, external tool sets, launch
overrides, trust and resume preparation, the session runner, event data
guards, a headless session driven by a scripted model, and the dependency
guard. Does not reference the host executable.

### CrystalCode.Server

The HTTP front end behind `crystal server`: ASP.NET Core Minimal API endpoints
and server-sent events on Kestrel, authentication and scopes, the explicit
wire mapping, the interaction broker, the event log, and the host that wires
`SessionRunner` to HTTP. One process hosts one active session. It holds no
product logic. The executable's `Commands/ServerCommand` parses the command
line, resolves the token, and hands off to it. The protocol is in
[docs/server.md](docs/server.md).

| Folder | Owns |
| :--- | :--- |
| `Hosting` | `CrystalServer` (Kestrel, startup, the listening line, warnings, shutdown), `ServerSession` (launch, the live `CodingSession`, `SessionRunner`, close), `ServerOptions`, `ServerRuntime`, `ListenAddress`, the discovery file, `ClientRegistry`, and the `clientRequestId` ledger |
| `Security` | `AccessGuard` (Host and Origin checks, CORS and exposed headers for listed origins, bearer tokens, stream tickets, failure rate limit; the lists come from `--allow-*`/`--expose-header` and the `server` section of `config.json`), `TokenStore` (hashed process-local tokens), scopes and the scope of each `SessionVerb`, problem responses |
| `Events` | `EventHub`: the session observer that numbers, serializes, buffers, and fans out events and owns the `SessionState` reducer |
| `Interactions` | `InteractionBroker`: approval, question, trust, session, and child prompts as interactions with validated answers |
| `Wire` | `WireWriter`, `WireEvents`, `SnapshotWriter`: every wire field written explicitly |
| `Endpoints` | Route groups for input and modes, images, quick prompts, side questions, children, export, interactions, tokens, and the event stream |

### CrystalCode.Server.Tests

Server tests against a real Kestrel listener on a loopback port and a scripted
model, the parity guards, the secret-leak test, and the dependency guard.

### CrystalCode.Bridge

The ACP front end behind `crystal bridge`: newline-delimited JSON-RPC 2.0 on
the raw stdin and stdout streams, the explicit ACP wire mapping, prompt
content blocks, and the host that wires `SessionLauncher` and `SessionRunner`
to one ACP session. One process hosts one active session. It holds no product
logic and never serializes an engine record by reflection. The executable's
`Commands/BridgeCommand` parses the command line, points `Console.Out` and the
Spectre console at stderr, maps SIGINT, SIGTERM, and SIGHUP to shutdown, and
hands off to `CrystalBridge.RunAsync`. The protocol and its phases are in
[docs/bridge.md](docs/bridge.md).

Which session options a client sees as config options is operator
configuration, not engine behavior. `BridgeHostSettings` (Engine
`Configuration`, read and written by `DiskSettingsStore` as the `bridge`
section of `config.json`) holds the `customConfigOptions` switch and the saved
id list, and the engine's option catalog is unchanged. `BridgeCommand`
resolves a flag, then the saved list while the switch is on, then the built-in
default, into the expanded id list on `BridgeOptions`. Bridge filters by id
at the wire (`SessionUpdates`, `BridgeAgent`) and never edits the catalog.
A hidden option is refused as `-32602` over ACP and stays a slash command.
The same section holds `workspaceTrust` (default off): whether Bridge asks
before entering an untrusted directory. `BridgeCommand` resolves
`--workspace-trust`, then that value, and hands the result to the launcher
as `SessionLaunchOptions.WorkspaceTrust`, so the engine's trust checks, also
inside the session, follow it. The terminal's top-level `workspaceTrust` is
not read for Bridge. Like `server`, the section is never written by a
command line.

| Folder | Owns |
| :--- | :--- |
| `Transport` | `JsonRpcConnection` (one reader, one ordered writer, concurrent requests, `$/cancel_request` both ways, parse and batch errors), the error codes, `JsonRpcException`, and request field readers |
| `Hosting` | `CrystalBridge` (the public entry), `BridgeOptions` (with the offered option ids), `BridgeAgent` (`initialize`, `session/new`, `load`, `resume`, `fork`, `list`, `prompt`, `cancel`, `set_mode`, `set_config_option`, `close`), `BridgeSession` (the live `CodingSession`, `SessionRunner`, the front-end contracts as forms, held updates, the ACP id that survives `/clear`, the `SessionState` behind `_crystal/snapshot`, child approval routing), `Extensions` (every `_crystal/*` method), `ReportCapture` (structured values for report requests), `ClientFeatures`, `LaunchTrustPrompt` (trust before a session exists), `SessionSetup`, and `TurnTracker` (which engine turn a prompt waits for, faults, clears) |
| `Prompts` | `PromptContent`: content blocks to submitted text, image attachment through the engine, file image links through `TryAttachImageFile`, and mention lines |
| `Wire` | `SessionUpdates` (events to `session/update` and `_crystal/*` notifications, compaction updates, archive replay, with the mapped, notified, and deliberately unmapped lists), `ChildUpdates` (subagent sessions for a client that opts in, the parent `task` call otherwise), `CrystalWire` (extension values with the server's field names), `ToolCallWire`, `PermissionWire`, `TurnWire`, `ConfigWire` (config options and legacy modes from `SessionOptionsChanged`), `FormWire` (elicitation forms and answers), `McpServerWire` (client MCP servers as session definitions), `SessionList` |

### CrystalCode.Bridge.Tests

Bridge tests over in-process pipes with a scripted model, the parity guards,
the secret-leak test, and the dependency guard.

### CrystalCode.Display

The terminal UI library. Owns the alternate-screen frame, input routing,
composer, transcript viewport, markdown paint, and queue card. Spectre
widgets are rasterized into frame rows. `AnsiConsole.Live` is not the
session shell. Terminal.Gui is a parked package reference for
supply-chain review and is not called. The host loop stays in this
assembly's shell types; a later migration to Terminal.Gui would replace
that loop inside this project only.

Committed transcript entries may carry a Spectre `IRenderable` alongside a
plain-text fallback. The display engine measures the widget at the current
terminal width when its cache is rebuilt, so tables, grids, panels, and other
high-level components participate in resize, scrollback, and repaint like
text. Non-interactive output renders the same widget directly. The shell
progress region owns transient indeterminate progress and elapsed time;
static command reports remain committed transcript widgets.

### CrystalCode.Display.Tests

Display tests: layout, chrome, input decoder, scroll policy, composer, paint,
transcript log, queue card, and the dependency guard. Does not reference the
host executable.

### CrystalCode.Providers

Model adapters. Each adapter implements `IChatClient` and, when the provider
can stream, `IStreamingChatClient`. Image-capable adapters additionally
implement `IStreamingMultimodalChatClient`. Provider options, wire DTOs, and
transport exceptions stay in this assembly. No tools, no UI, no home-directory
layout.

### CrystalCode.Tests

Terminal host tests: session renderer, event-to-slash-option mapping, status
and tool-list widgets, tool-call and progress text, transcript replay, the
session picker, approval cards and keys, the image-marker contract with
Display, and `crystal run` against a scripted model.

### CrystalCode.Providers.Tests

Provider adapter tests. One folder per vendor, mirroring
`CrystalCode.Providers`. Does not reference the host executable.

## Namespace ownership

Root identifier is `CrystalCode`, matching the existing solution. Folder
path under a project root equals the namespace after the project name, so
engine namespaces read `CrystalCode.Engine.Sessions` and so on.

CrystalCode (executable):

| Folder | Owns |
| :--- | :--- |
| `Commands` | Spectre.Console.Cli commands. Bare `crystal` stays the terminal. `space` opens the operator space under the data directory. `run` is the headless task command. `server` resolves the server token (`ServerTokenSource`) and signals (`ShutdownSignals`) and starts `CrystalCode.Server`. `bridge` captures the raw stdio streams, rebinds console output to stderr, registers signals, opens the optional `--debug` sink (stderr or an appended file), and starts `CrystalCode.Bridge`. `version` prints the build identity |
| `Run` | Unattended front end for `crystal run`: plain-text or JSON-lines log, denied operator prompts, unsupported questions, and exit codes. Overrides and launch preparation come from `CrystalCode.Engine.Launch` |
| `Terminal` | Host loop, event projection, session renderer, status and tool-list widgets, progress and tool-call text (`ToolCallText` reveals hidden characters in the engine's `ToolCallSummary`), transcript replay, session picker, question overlay, startup trust and resume prompts (`StartupPrompt`), slash-option mapping |
| `Terminal/Approvals` | Approval prompt, card, diff preview, and keys |

CrystalCode.Engine (namespaces below are relative to `CrystalCode.Engine`):

| Folder | Owns |
| :--- | :--- |
| `Configuration` | Loaded options, defaults, thinking and mode labels, display-case helpers |
| `Events` | Immutable session events, observer interface, chrome and preference snapshots |
| `Home` | `~/.crystal` paths and file I/O; `DiskSettingsStore` is the default `config.json` implementation and reads provider files through `ProviderFiles` |
| `Home/Interfaces` | Configuration persistence contracts such as `ISettingsStore` |
| `Launch` | Launch options a front end received (`SessionLaunchOptions`), in-memory overrides (`SessionLaunchOverrides`), `--allow-read` / `--allow-write` rules (`SessionLaunchAccess`), and `SessionLauncher`, which resolves the workspace, resume target, trust, prompt attachments, and mode before a session exists |
| `Sessions` | Session (`CodingSession`), transcript, ledger, streaming turn, slash commands and completions, chat client lifetime, status, usage, and tool-list text, the input loop for front ends without a frame loop (`SessionRunner`), the front-end-neutral reducer (`SessionState`, `SessionSnapshot`, `LiveRound`, `ChildSessionFold`), front-end contract (`SessionFrontEnd`, `ISessionChooser`) |
| `Approvals` | Risk, authority, grants, policy, review transcript |
| `Approvals/Interfaces` | Prompt and reviewer contracts |
| `Compaction` | Window accounting and summary substitution |
| `Catalog` | Plain-text column padding shared by inventory reports (`AlignedRows`). Not a model, tool, or plugin catalog |
| `Tools` | Workspace fence and built-in `ITool` types |
| `Tools/External` | Operator tool sets (`tools.json`), exec and isolated `ITool` / `IMultimodalTool` loaders, and host-context dispatch |
| `Prompts` | Caller-owned system text. Built-in Work and Plan identify the assistant as Crystal Code |
| `Modes` | Operator-defined custom modes: `modes/<name>` discovery, tool selection, prompt composition, and launch-name validation |
| `Notifications` | Operator notification: `OperatorNotifier` runs the `operatorNotify` command, and `NotifyingApprovalPrompt` and `NotifyingUserPrompt` wrap the front-end prompts so every operator wait reaches it |
| `Skills` | OpenCode-compatible `SKILL.md` discovery and catalog |
| `Plugins` | In-process registry and built-in contributions |
| `Plugins/Interfaces` | Contribution contracts |
| `Plugins/Providers` | Built-in DeepSeek, OpenAI-compatible, Responses, Anthropic, Gemini, and Ollama client factories |
| `Version` | Build identity for `crystal version`: product commit, Crystal commit, compiling SDK, compile configuration, host runtime, and host operating system. Two plain-text sections |

CrystalCode.Display:

| Folder | Owns |
| :--- | :--- |
| `Input` | Decode ReadKey bursts into keys, paste, and wheel events |
| `Shell` | Alternate screen, painter, layout, key burst, scroll policy, status and progress chrome |
| `Composer` | Prompt buffer, keys, slash picker |
| `Cards` | Queue overlay |
| `Transcript` | Viewport log, role cards, sequential fallback |
| `Paint` | Markup, markdown, theme, wrapping |

Quick prompts keep the same dependency direction: Engine Home owns
`QuickPromptStore`, bounded UTF-8 file reads, validation, and immutable catalog
values; Display Composer owns the isolated quick-prompt picker and preview.
The terminal maps catalog values, routes F6 and a bare `/quick` only outside
modal prompts (other front ends only get a note from `SessionVerb.Quick`), and
inserts Fill at the current composer cursor. Send uses
`CodingSession.SubmitQuickPromptAsync`, bypasses slash parsing, and never
consumes pending or composer draft images. Ordinary queued submissions bind
pending images at enqueue time so later quick sends cannot steal them. Quick
queue entries do not request cancellation after tools; explicit queue release
and ordinary follow-ups retain their existing interrupt behavior.

Provider types live under `CrystalCode.Providers` plus one folder per wire
family (`DeepSeek`, `OpenAI`, `Responses`, `Anthropic`, `Gemini`, and `Ollama`). Shared
OpenAI-compatible Chat Completions request and stream parsing lives in
`Compatible`. Shared direct-HTTP lifecycle and SSE framing for Responses and
Messages, Gemini GenerateContent, and Ollama Chat lives in `Protocol`; each
wire codec still owns its request and event semantics. Gemini uses SSE and
replays signed response parts so function calls retain thought signatures.
Opaque reasoning state is provider-owned: an adapter replays state only in
its own format. A block it cannot replay keeps its readable text instead
of failing (assistant text, or the protocol's readable reasoning field
where it has one).
Ollama uses JSON lines and native `tool_name` results. Anthropic Messages
marks prompt-cache breakpoints (`cache_control: ephemeral`) on the last
system block and the last message content block of every request, so the
tool definitions, the system prompt, and the conversation prefix written by
the previous round are read from the provider's prompt cache; the multimodal
codec keeps the message breakpoint when image expansion splits a marked text
block. Other adapters rely on their providers' automatic prefix caching and
send no cache directives. Chat Completions, Responses, Anthropic Messages,
Gemini, and Ollama map an `error` payload, an event whose `type` is `error`,
and a JSON body returned where an event stream or JSON lines were expected to
provider faults carrying the provider code and message. Gemini uses
`error.status` when `error.code` is a number. A non-streaming JSON response
with an `error` payload maps the same way even when the HTTP status is 200.
Anthropic Messages reports uncached input, cache writes, and cache reads as
separate counts. The adapter sums them into the one input count Crystal
exposes, because the session reads that count as the prompt size. A usage
figure the endpoint did not report is absent, never zero, and an event
stream without any usage emits no usage event. A stream may report usage
more than once; each report is a cumulative snapshot of the attempt and the
latest one wins. The Messages adapter reports as soon as `message_start`
carries the input side, so a request that breaks mid-stream still charges
its input. A Messages stream that
reaches `message_stop` without a `stop_reason` in a `message_delta` fails as
a truncated stream rather than completing as a normal stop. An event or
response whose fields are missing or mistyped fails as an Anthropic fault
instead of leaking a JSON reader exception. The Messages adapter maps
`max_tokens` and `model_context_window_exceeded` to `length`, and `refusal`
to a content filter, and `pause_turn` to the provider finish reason
`ProviderFinishReason.PauseTurn`. The adapter declares no server tools, but
a gateway or later request may run them. Blocks the provider ran itself
(`server_tool_use`, `mcp_tool_use`, `container_upload`, and every
`*_tool_result`, including web search results with their encrypted
content) are never surfaced as tool calls. They are kept as opaque
reasoning state under the `anthropic.messages.server_block` format, in
stream order and with streamed input assembled, and written back
verbatim on the next request. Other providers drop them as non-replayable
state without readable text. A replayed block carries no cache marker, so
the moving breakpoint sits on the last block that can hold one.
Framed Responses and Anthropic error events keep the provider message as the
fault text. Session retry then reports the cause instead of treating the
truncated stream as retryable. Outbound chat requests send a constant `User-Agent` of `Crystal Code`
with no version. Provider SDKs are not used.

## Crystal consumption

The interactive turn uses `IStreamingChatClient` and `ToolExecutor`. When the
selected model declares `imageInput` and its adapter supports it, the session
uses `IStreamingMultimodalChatClient` and a host executor that preserves the
same approval policy. The session owns those clients and may replace them on
`/model`. It does
not use `Crystal.Agents.Agent` for the live UI: that Agent completes model
turns without token streaming.

The live parent keeps its streaming loop. Child invocations consume
`Crystal.Harness.AgentHarness` or its multimodal counterpart through local
Agent adapters that reuse the host streaming turns. The host owns prompts,
tool catalogs, approval, compaction, persistence, and terminal projection.

### Child invocation ownership

An Agent definition, a persisted child session, and a Harness invocation are
three identities. Every `task` without `sessionId` creates a child; continuation
uses that child id and a fresh invocation id. Concurrent continuation of the
same child is rejected. The parent is not an `IAgent`: direct child invocations
are depth zero inside Harness, whose `MaximumDepth` is zero. Child catalogs
also remove `task`, `question`, and background Bash tools.

`CodingSession` binds child execution, configuration, and commands through its
Sessions components. Improving that configuration is medium-priority product
work and is not in this build. Current manifests, `/agents`, and process
overrides stand. Which settings change is not defined, and no new
configuration field or command is reserved. `AgentCatalog` loads bounded
manifests and prompts from Home and workspace; an invalid or disabled
workspace directory shadows the Home definition. `AgentToolsPlugin`
contributes the session-bound task tool through the existing plugin
registry. Ordinary tool catalogs are built first;
child catalogs intersect their definition snapshot with the current parent
ceiling before constructing approved executors. A plugin hook cannot restore
a tool absent from that executor's catalog. Child todos and transcripts are
independent. The filesystem remains shared.

The Engine holds one `SharedTurnBudget` per parent turn. Text and multimodal
turns atomically reserve logical model rounds and whole tool batches from it.
Retries stay inside a round reservation. Child compaction reserves a model
call only when its summary client is actually invoked. The parent's turn timer
and cancellation token also cover child queue waits, approvals, and backoff.
Harness receives per-invocation limits and enforces invocation lifecycle and
depth. Its whole-run reservation model is not used as a second copy of the
parent's remaining quota; otherwise the first parallel invocation could reserve
all capacity, and text and image Harness instances would spend separate pools.

`TaskBatchExecutor` runs only consecutive task calls in parallel, retaining
input result order. Every other tool is an execution barrier. The same grouping
is preserved by the multimodal bridge. A session semaphore counts active
children rather than Agent types; each child also has single-invocation
admission. The parent's approval policy is shared, and a semaphore serializes
classification and review. A separate operator gate serializes the operator
approval card with the child picker, so a pending review never delays the
picker. A scoped child label reaches the approval card, and review sees the
child's context. No state lock is held while waiting for approval or a
concurrency slot.

Child clients are cached independently of parent, review, and compaction
clients by resolved provider, model, service tier, speed, and fitted output cap.
Changing thinking does not create a client. Parent model changes leave child
clients alive. Child images require both model `imageInput` and adapter image
capabilities. The multimodal path reuses `MultimodalStreamingTurn` and the
approved bridge, allocates media numbers from the parent, and supplies only
that child's image map to its model. Text children never expose native image
tools. A child summarizer uses its own context budget and the configured
compaction prompt overlays; a configured compaction model is honored.

Each completed invocation updates its child ledger and the parent ledger once.
Parallel increments and saved snapshots are serialized. Live cumulative usage
uses per-child snapshots rather than adding every stream update. The parent's
last-request usage stays its own; child usage never replaces it. Missing usage
retains the existing unknown-total behavior. Failed retry attempts contribute
only their last reported usage snapshot. `ContextEstimated` carries local
estimates independently of provider `TokenUsage`.

`SessionDocument.Children` embeds child archives, compacted items, model and
gear snapshots, todos, usage, notes, and media references. Checkpoints taken while a parent waits for
children use its live model context, not the pre-turn transcript. Pending tool
calls receive checkpoint-only results identifying completed children or warning
that an interrupted operation may already have effects. These results never
modify the live turn. Children do not create
session files or appear in saved-session lists. Fork deep-copies child state,
resume restores counters directly, and clear removes it. Saved running or
queued children become interrupted rather than restarting side effects.
Child media is persisted before the parent JSON reference. Export inlines
available child media using the existing store.

`ChildSessionChanged`, `ChildEventReceived`, and `ChildViewRequested` convey
immutable event records; the latter is an inspection request, not engine focus.
The terminal owns parent/child event projections, the F7 child picker, read-only
child input routing, composer visibility, and the focused model/usage chrome.
Inactive events remain available when switching views. `ApprovalPassed` and
`ApprovalReviewing` (which names the primary or extended stage) use the same source-scoped event path; terminal approval
callbacks own only the shared modal, never the focused transcript or progress.
Runtime approval cards survive child completion and view switches without
restarting completed progress. Child tool batches publish `TodosChanged` and
refresh their stored todo snapshot before the next model round. The input pump
awaits `CodingSession.EnqueueAsync` for child inspection instead of discarding
an asynchronous command through the synchronous queue path. Parent events are
cached without being projected into a focused child, and restoring the parent
replays them without replaying submissions into the composer. Shared preferences,
completions, prompt history, image invalidation, queue state, plugin status, and
stats/side-question panel requests are applied once outside conversation replay.
Switching views cannot re-seed old prompt history, invalidate new draft image
attachments, or reopen dismissed panels. Live child usage and todo events update
the terminal's picker snapshots without rebuilding the focused transcript. The
pickers refresh through the existing input polling loop and retain selection by
child id across active-status sorting. The read-only composer region shows the
child identity and Esc/F7 hints; the model status field retains the model name.
A shared modal retains input ownership during shortcut returns. Disabling Agents clears
the terminal's child projection and navigation affordance. The terminal never
submits text to a child. Headless JSON labels child events and media provenance;
text output keeps the returned summary with the task result and reports child
usage separately.

Built-in child prompts are `agents/general`, `agents/work`, and `agents/plan`.
They do not reuse the parent Work or Plan body or those attachments. `general`
uses `agents/general`, and `agents/plan` when the parent turn is Plan. `work`
uses `agents/work` and is not offered during a parent Plan turn. `explore`
always uses `agents/plan`. Resolution
follows the named-prompt order, then appends only that agent's attachments,
then the built-in child safety and role lines. Custom Agent `prompt.md` files
are not replaced or extended by prompt overlays, prompt sets, or prompt
attachments. They still receive the child safety rules and bound placeholders.

Approval is a `ToolInvocationPolicy` supplied to `ToolExecutor`. Rejection
returns Harness-authored `ToolOutput`. Tool exceptions become model-visible
only through a Harness `ToolExceptionMapper`.

Ordinary tool-call batches run serially. Work and Plan construct `ToolExecutor`
with serial mode and concurrency one. A host wrapper permits only adjacent
`task` groups to overlap, in both text and multimodal paths. Configurable
concurrency for ordinary tools remains deferred; do not reserve a field for it.

### Image input boundary

CrystalCode.Engine owns image attachment, MIME signature validation,
session persistence, transcript markers, and projection onto Crystal's typed
multimodal contracts. A front end owns only how it presents and edits the
markers in its composer. `[Image #N]` is presentation and persistence metadata;
providers receive typed `ImageContent`, never a marker in place of its bytes.
Pasted markers are atomic composer spans with a distinct color: cursor movement
and deletion cannot leave a partial attached marker. User-entered text with the
same spelling remains plain text. On submission, only attached spans receive an
invisible provenance prefix; only prefixed markers in transcript items produce
model images. The prefix is removed for display and text-only model requests.
Session JSON records whether markers have this prefix. Older sessions without
that flag tag references to their saved images on load, preserving their prior
semantics. Deleted unsent images are discarded immediately while idle and at
submission during an active turn. Workspace and clipboard images
are checked against the 20 MiB limit and validated by MIME signature before
they enter the session; the media store validates again before writing. Saved
image bytes live in owner-only, content-addressed files under
`~/.crystal/media/<sha256>`. Session JSON stores the hash and MIME type, not
Base64 bytes. The media file is written before the session references it, and
both writes use a temporary file followed by an atomic replacement. Sessions
persist only images referenced by the archive or the model context. A missing, truncated, or
modified media file is skipped on resume while the text and other attachments
remain available; the operator receives a note, and its reference is retained
on later saves so restoring the file can repair the session. Inline `data` is
a supported session representation; local saves move its image bytes to the
media store.
JSON session export retains marker provenance and inlines available image bytes
in `data` so its contents do
not depend on Home media files. If a referenced media file is unavailable, its
metadata remains in the export but bytes cannot be included. The store does
not automatically delete unreferenced media files.
Inline image bytes and absolute image URIs are supported. Images returned by
an in-process plugin, a dotnet operator tool's native `IMultimodalTool` path,
or an exec tool with `"output": "content"` are assigned the same markers and
become input on the following model round. A text-only turn rejects exec
image results instead of dropping them.
Clipboard paste reserves its image number before waiting for the OS reader.
Tool images reserve numbers through the same session lock, which also protects
image lookup and mutation. A failed paste leaves a gap rather than reusing its
number. Input batches dispatch keys in order, so Ctrl+V finishes attaching
before a following Enter submits the prompt.
Dotnet multimodal tools are omitted from active catalogs when the selected
model or provider does not support image input. Declared `audioInput` or
`videoInput` does not keep them.
`describe_image` is a text tool on Plan and Work when `config.json`
`imageModel` names a provider and model whose catalog entry has
`imageInput`. It sends that file to a side multimodal client with the
image-description prompt and returns the reply as text. The side
request has no tools, is not written into the session transcript, and uses
`imageModel.thinkingEffort` through the same gear mapping as the host.
`view_image` is registered only for an image-capable turn. It returns the
file as `ImageContent` so the current model sees it on the next round.
Both follow the `read` path fence. A missing image-model credential fails
the tool call and does not block session start.

The Responses, OpenAI-compatible Chat Completions, DeepSeek Chat Completions,
Anthropic Messages, Gemini GenerateContent, and Ollama Chat adapters accept
text and image input and emit only text, reasoning, and tool-call events.
The native Gemini adapter sends tool-result images as `inlineData` parts on
`functionResponse`. That is the Gemini 3 multimodal function-response field;
an older model may reject the request. The native Ollama adapter sends them
on the tool message `images` array. Transcripts keep markers either way.
Chat Completions and Anthropic images
are restricted to user and tool messages, matching their wire contracts. Clipboard
image input is read through Windows PowerShell on Windows, the macOS system
`osascript` command on macOS, and `wl-paste` or `xclip` on Linux; it does not
capture the screen. Windows reads clipboard PNG bytes before falling back to
bitmap conversion, and passes Base64 text from PowerShell to the host. macOS
tries PNG, JPEG, and GIF clipboard representations in that order. Linux checks
advertised clipboard types and tries PNG, JPEG, GIF, and WebP in that order.
Reader output and temporary files are bounded by the image size limit. A
missing reader, an empty image clipboard, unsupported image bytes, an oversized
image, and a reader failure have separate operator errors. Reader commands are
optional at runtime, and their absence
does not prevent the application from starting. A model must
opt in with `imageInput: true`; unsupported combinations are rejected before
sending a request. `audioInput` and `videoInput` are stored declarations.
They are published on plugin model facts and on `PluginClientRequest`, and
they do not change adapter capabilities, tool registration, attachment,
persistence, or the outbound request. Selected service tiers and speeds are
also published on both contracts; a custom protocol factory must apply a
non-null slot or reject it when the client is created. Built-in adapters
still accept only text and image input and emit only text, reasoning, and
tool calls. Accepting audio or video, and audio or video model output, remain
TODO. Image generation is high-priority product work and is not in this build;
see BUSINESS.md. MCP
media transport is TODO.

CrystalCode does not capture screenshots, control browsers, phones, virtual
machines, interpret coordinates, or define frame/device protocols. External
plugins own those behaviors and may return ordinary typed images through the
generic tool boundary.

## Session and turn

One user message is one turn:

1. Snapshot the transcript and current tool definitions.
2. Stream one chat request. Publish each delta as an event as it arrives. If that
   round fails with a retryable provider error (HTTP 429, 404, 408,
   5xx, timeout, network, or an incomplete stream), wait with
   exponential backoff and repeat the same round. The `retry` policy in
   `config.json` sets the limit and timing (defaults: five retries, 2s
   base, factor 2, 25% jitter, 30s cap when Retry-After is absent);
   Retry-After or retry-after-ms is honored when present. The engine
   publishes `RetryScheduled`; the terminal progress row shows
   `Retrying In Ns (Attempt K)` and counts the remaining wait down, and
   the transcript prints `retrying model request` plus the operator
   message. Live stream text from the failed attempt is
   discarded. HTTP 401, 403, quota, `invalid_prompt`, and context
   overflow are not retried. A connection that closes while the request
   body is still being written is retried once, so a stale connection can
   be replaced, and then reported with the transport cause instead of
   using the rest of the budget. User cancel still interrupts immediately.
3. Select candidate zero.
4. Resume when the finish reason is `ProviderFinishReason.PauseTurn`: the
   provider paused a server-side tool loop. Append the candidate's items
   without its tool calls, publish no stop, and start the next round, so
   the paused content is the last assistant turn the provider sees. Tools
   do not run on a paused round. Five pauses in a row without a different
   round in between fail the turn and publish
   `Model paused the turn too many times in a row.` Each pause counts as a
   model call against the turn limits.
   Stop before tools when the finish reason is not a normal stop or a tool
   request. `length` keeps the assistant text, drops tool calls, and stops
   as `output_truncated`. A content filter keeps the text and stops as
   `content_filtered`. Any other reason stops as `failed` and publishes
   `Model stopped with finish reason '<value>'.` These stops do not compact.
5. If the candidate has tool calls, execute the full batch through
   `ToolExecutor`, or the multimodal bridge for an image-capable turn
   (approval runs first in either case). `CallScreeningExecutor` sits in front
   of `ToolExecutor` because Crystal rejects a whole batch when one call names
   an unregistered tool or has the wrong input kind. A bad call gets its own
   failed result, naming the available tools, and the other calls in the
   batch still run in order. A JSON call with no argument text is recorded
   with `{}`, since a model that calls a tool with no parameters often sends
   nothing and every provider codec that replays the call parses it as
   JSON. Sessions saved before this read an empty JSON argument as `{}`.
   Text-input arguments are never rewritten. A batch that does not fit the
   remaining tool-call budget stops as `tool_call_limit_reached`, keeps
   the assistant text and reasoning, and leaves those calls out of the
   transcript.
6. Append exact `ToolResult` values.
7. Repeat until the candidate has no tool calls, a configured limit stops
   the turn, or the user cancels. Before each model round, compact if the
   estimated transcript or the last model-round usage is over budget. One
   failed compact while still over budget stops the turn
   (`context_overflow`). A provider failure that survives retry stops the
   turn as `failed`: the error is published, the progress row clears, and
   the transcript, including that user message, is saved.

Text and image-capable turns share `executionBudget` from `config.json`.
The default per-turn limits are 1024 model calls, 8192 tool calls, and 7 days.
The setting may be `"unlimited"` for all three, or an object with optional
`maximumModelCalls`, `maximumToolCalls`, and `maximumDurationSeconds` fields.
Omitted fields keep their defaults; `null` removes that field's limit.
Model calls and duration must be positive when finite. Tool calls may be zero
to prevent tool execution. Duration must fit the runtime timer. A turn with
unlimited limits still ends on model completion, user cancellation, or context
compaction exhaustion. This host owns the live turn budget; it follows the
nullable-limit semantics of Crystal's `AgentRunLimits` without using Agent
for streaming UI turns.

Built-in `bash` has its own per-command timer, `bashTimeoutSeconds` in
`config.json`. Omitting it keeps 600 seconds. `null` or `"unlimited"` disables
that timer. A positive integer up to the runtime timer limit replaces it.
User cancellation and the turn budget still stop the command. External tool
sets keep their own `timeoutSeconds`, which defaults to 120 seconds and does
not follow `bashTimeoutSeconds`.
7. After a completed turn, consider compaction from that last round's
   reported usage plus an estimate of anything added after it. `/compact` (alias
   `/summarize`) summarizes earlier turns immediately, without waiting for
   that threshold. Compaction runs
   inside an engine call the front end awaits; the front end keeps its
   own surface live meanwhile (the terminal host pumps the frame so the
   spinner, resize, and composer stay live).

The session keeps two transcripts. The archive appends user text, assistant
text, reasoning, tool calls, and tool results as they are committed, and
compaction does not change it. The model context is the list sent to the
model. Compaction replaces that list with the live system prompt, a summary,
and a recent tail. Usage, approval review, and later model calls read the
model context. Resume, fork replay, and export read the archive. `/stats`
tool share reads the archive, and a session file with no archive counts tools
from its saved items. A session
file written before the archive existed loads its saved items as the archive.
A compaction summary that the operator sees (per `showCompactionSummary` and
`showAutomaticCompactionSummary`) is also appended to the archive as an
`## Earlier context` system message, so fork replay, resume, and export show
the same block again. A hidden summary is not archived. Side questions skip
archived summaries that follow a turn.
Images stay when either transcript still mentions them. `/clear` drops both.

The composer stays open while a turn runs. Enter with text enqueues a
follow-up (FIFO). Queued items stay in a panel above the composer until
they are sent. The queue is sent when the current tool batch finishes or
when the turn (thinking or conversation) ends. Empty Enter while a
follow-up is queued interrupts the turn, or compaction if that is still
running, so the queue is sent now. Empty Enter with nothing queued does
not stop the turn. `/quit`, `/clear`, `/resume`, and `/fork` stop a busy
turn and then run, including a command submitted while other session work
is still on the stack. `/clear`, `/resume`, and `/fork` stay in the queue
and run in the order typed. `/clear`, `/resume`, and `/fork` drop queued
follow-up text; the panel updates and does not keep it. During compaction
those three except `/quit` are refused, as are other commands that change
the session, workspace, or model.
`/status`, `/help`, `/todos`, and other commands that only report state or
adjust the display run immediately. `/quit` cancels compaction and exits.
Ctrl+C interrupts and does not drop queued text.
At an idle prompt, Ctrl+C clears the composer.
Two Ctrl+C presses on an empty composer exit.

The terminal registers SIGTERM and SIGHUP through `PosixSignalRegistration`
(on Windows these are the console shutdown and close events). SIGINT stays
with the Ctrl+C rules above. The first signal cancels the default termination,
interrupts the running turn or compaction, and stops the key loop. The loop
collects an interrupted turn with no cancellation token, so the plugin
turn-end hook and the transcript update are not skipped, and then the
session closes and saves as on `/quit`. The handler holds the process open
for up to four seconds while that runs, because Windows ends the process
when a close or shutdown handler returns. A second signal uses the default
termination. SIGKILL and power loss cannot be handled; the turn checkpoints
cover them. This handling lives in the executable. The engine has no signal
code.

`/btw` (alias `/side`) asks one side question from the archive
and the newest 20 side exchanges in this process. The request uses the work
model, the current system message, an empty tool list, and its own cancellation.
When that request does not fit the window, the host compacts a copy and does
not write the summary back. If the copy still does not fit, the side question
fails.
Text still streaming in the current round is not included. A tool call is not
executed. Text that arrived with it is kept; a tool call with no text fails,
and the question stays in the panel so it can be asked again. The question,
the answer, and the token usage stay out of the queue, the transcript, the
usage ledger, compaction, and the saved session. The terminal draws the
answer in a panel above the composer and leaves the status bar, transcript,
and progress row in place. Until reasoning starts, that panel shows
Waiting for the model with the same one-cell spinner as the progress row.
Once reasoning starts and before answer text, it shows Thinking with that
spinner, and the frame keeps advancing while the panel is open. Esc, Enter,
or Space closes the panel. Left and Right step through earlier answers. `x`
clears the in-memory thread. While that panel is open, Ctrl+C cancels an
in-flight side question, closes the panel, and leaves the main turn running.
`/clear`, `/resume`, and
`/fork` drop the thread. `crystal run` rejects the command with every other
slash command.

## Plan and Work

These are product modes, not Crystal types. Plan and Work are built in.
Operator-defined custom modes sit beside them (see Custom modes below).
Prompt overlays, prompt sets, prompt attachments, and external tool catalogs
do not add a mode.

- Plan registers read, list, tree, glob, grep, search, todowrite, todoread, and
  question. When Skills is enabled, it also registers skill. External tools
  whose `catalogs` include `plan` are appended after the built-ins.
- Work registers read, list, tree, glob, grep, search, todowrite, todoread,
  question, and skill when enabled, then edit, write, apply_patch, bash, and
  the session-local `bash_start`, `bash_status`, `bash_logs`, and `bash_stop`
  tools, then external tools whose `catalogs` include `work`.

CrystalCode currently maps only JSON tool input. Built-in and external dotnet
tools publish `ToolDefinition.Json`; a dotnet tool set that publishes
`ToolDefinition.Text` is skipped with a visible note. Provider adapters reject
a text-input tool or a text tool call in history with `NotSupportedException`
instead of wrapping it. Session documents and the plugin model transcript
preserve `ToolInputKind`, but no current provider maps `ToolInputKind.Text`.
Provider-specific custom-format tools such as OpenAI's `apply_patch` take
text input and are medium-priority product work, not part of this build.

`config.json` `disabledBuiltInTools` stores Home-level persistent built-in tool
disablement as an object with separate `plan` and `work` arrays. A legacy array
is accepted and applies to both modes. `/tools builtin show` displays both
lists; `/tools builtin plan|work show|enable|disable <name>` changes one list
while idle, rebuilds both executors, and refreshes the live system message. An
unknown name is rejected. `--disable-tool` repeats on `crystal`, `crystal
space`, and `crystal run`; an unqualified name applies to both modes, while
`plan:<name>` or `work:<name>` targets one mode. Launch overrides are not
saved, and enabling a Home name does not remove a process-only disablement.
The active mode's merged list omits the tool from its Plan or Work catalog,
including `describe_image`, `skill`, `view_image`, and named background shell
tools. Work and Plan prompt templates expose that list through the host-owned
`{{disabled_tools}}` placeholder; custom prompt text must include the token if
it wants the guidance. The name stays reserved for plugins and external
tools.

`/tools builtin session [plan|work] show|enable|disable <name>` keeps a third
list that lives in `SessionDocument.disabledBuiltInTools`, not in `config.json`
and not in `HarnessSettings` persistence. The active list is the union of Home,
process, and session names. Changes are refused during a turn or compaction,
rebuild both executors, refresh the live system message, and save the session.
Resume restores the saved list and drops a name this build no longer reserves.
`/clear` empties it. Fork does not copy it, like directory access. A workspace
change keeps it because the session continues. Child Agents see the merged
list. `/tools builtin show` still lists only Home names.

Host-owned numeric limits are a separate pending question.

### Custom modes

`ModeCatalog` (`CrystalCode.Engine/Modes`) loads `modes/<name>/mode.json` and
`prompt.md` from `~/.crystal` and `<workspace>/.crystal`, Home first. A
workspace directory of the same name replaces the Home entry as a whole, even
when it is disabled or invalid. A directory name must satisfy
`ExternalToolNames.IsDirectoryName`; `plan`, `work`, and `default` are
reserved. Files must be regular UTF-8 files of at most 64 KiB, not symbolic
links, and at most 256 directories are read per root. `mode.json` has
`description` (required), `enabled` (default false), and `tools`, an array of
built-in tool names. Every name must satisfy `ExternalToolNames.IsReserved`;
an unknown name skips the mode with a note. `prompt.md` is the system body and
uses the same `{{name}}` placeholders as Work and Plan, plus `{{mode}}` set to
the mode name. It is not replaced by prompt sets, direct prompt overrides, or
prompt attachments. The host appends `{{env}}`, `{{skills}}`,
`{{instructions_section}}`, `{{disabled_tools}}`, `{{clock}}`, and
`{{session}}` when the body does not name that slot, so a short prompt still receives the same workspace facts as
Plan and Work.

A custom mode's catalog is built from the built-in tool set for Work, then
filtered to the listed names, then the merged disabled list for Work is
applied. Disk plugin, external, and MCP tools join only when their `catalogs`
include the mode's base: `work` when the mode lists any of `edit`, `write`,
`apply_patch`, or `bash`, otherwise `plan`. This keeps the Write + Workspace floor of an external
tool from appearing in a read-only mode. `task` is offered when Agents are
enabled and `task` is listed. A child launched from a custom mode always uses
the Plan child ceiling, even when the mode lists write tools.

A mode changes only the system message and the executor catalog. Approval is
unchanged: `ApprovalPolicy` classifies by model-facing tool name, so listing
`bash` never lets a call skip review or the operator. A tool the mode does
not list is not registered, so a call to it comes back to the model as a
failed result for that call, as a call to `bash` does in Plan.
`SessionDocument.Mode` stores the mode name and `PlanMode` stays written as
true only for Plan, so an older build reads a custom mode as Work. A session
that names a missing or disabled mode resumes in Work and writes a note.
`CodingSession.CycleMode` replaces Tab and Shift+Tab and walks Plan, Work, then
enabled custom modes in name order. `/mode` lists modes and `/mode <name>`
switches while idle. `--mode <name>` applies for one process, is rejected when
combined with `--plan` or `--work`, and is never written to `config.json`.
Disabled-tool lists remain `plan` and `work`; a custom mode uses the Work list.
`PluginModelPurpose` is Work for a custom mode, and `PluginTurn.Mode` carries
the mode name.

The built-in `question` tool accepts an ordered question array. Each question
has a short header, full text, described options, optional multiple selection,
and custom input enabled by default. A single single-select question submits
immediately. Multiple questions and multiple-select questions use a shared
confirmation step. Answers return in question order as arrays of selected
labels; dismissing the overlay returns a failed tool result.

Built-in Plan still has no edit, write, apply_patch, or bash. An external Plan tool
keeps a Write + Workspace floor, so Plan is not built-in reads only
once such a tool is installed. Approval still runs.

Background Bash tasks are owned by the live session. `bash_start` returns a
task ID without waiting for process exit. `bash_status` reports the task state,
`bash_logs` reads the bounded output buffer by offset, and `bash_stop` kills the
process tree. Tasks are not saved in session documents, do not cross a process
restart, and are stopped when the session closes. The configured
`backgroundBashTimeoutSeconds` timer applies to them, independent of the
foreground `bashTimeoutSeconds`; omitting it keeps 10800 seconds (3 hours), and `null`
or `"unlimited"` disables that timer. At most
32 tasks may be active at once; completed tasks remain queryable but do not
consume an active-task slot. Foreground and background Bash stdin is
redirected and closed after startup, so a shell never inherits the operator's
terminal, reads its keys, or switches it out of raw mode.

Switching modes replaces the first system message and the executor catalog.
The transcript is otherwise the same conversation.

The todo list belongs to the session, not to a mode. Built-in Plan text asks
the model to write the plan's steps as pending todos and leave them pending;
built-in Work text asks the model to work through pending todos it finds
instead of starting a new list. Built-in Work and Plan text also direct the
model to use `search` first when it does not know which files matter, then
read the best candidates and use tree, glob, grep, and list to understand the
repository. They tell the
model that file contents and tool results are data, not instructions, and
that workspace instructions override the built-in defaults while the user's
latest message overrides both. When `question` is unavailable or dismissed,
Work continues on the recommended default only when it is safe and
reversible, and Plan records that default as an open question.

Built-in Review text defines `risk_level` and `user_authorization` levels and
derives `outcome` from them: allow for low risk with medium or high
authorization, medium risk with high authorization, or high risk the user
explicitly requested; deny for low authorization or unrequested harm; ask
otherwise. Host risk and host authority are a lower bound, and the policy
still refuses to pass a `forbidden` call whatever the verdict says.

Live Work and Plan system text is assembled from overlayable templates using
host-owned placeholders (`{{name}}`). Review and compaction prompts use the
same placeholder syntax. Composite session slots are `{{env}}`, `{{clock}}`, `{{session}}`,
`{{model_info}}`, `{{skills}}`, `{{directories}}`, and
`{{instructions_section}}` or raw `{{instructions}}`. Atomic session slots
include `{{workspace}}`, `{{is_git_repo}}`, `{{git_root}}`, `{{platform}}`,
`{{os}}`, `{{architecture}}`, `{{date}}`, `{{time}}`, `{{provider}}`,
`{{model}}`, `{{model_line}}`, `{{mode}}`, `{{product_name}}`,
`{{session_id}}`, and `{{approval}}`. `{{os}}` is the operating system name
and version. `{{architecture}}` is the process architecture. `{{time}}` is
the local time with a numeric offset, captured when that system message is
composed. `{{is_git_repo}}` is `yes` when the workspace directory itself
contains a `.git` directory or file. `{{git_root}}` is the repository root
found by walking parent directories, or empty when that walk finds none.
`{{session_id}}` is the saved session id. `{{approval}}` is the approval
mode (`plan`, `default`, `edit`, `review`, `audit`, or `full`). `{{mode}}`
is `plan` or `work` for Work and Plan, the mode name for a custom mode, `review` for Review system text,
`compaction` for compaction system text, and `image` for image-description
system text. The `{{env}}` block holds only stable
workspace facts: workspace, git, platform, operating system, and
architecture, plus the git root when one is found. It carries no provider or
model. `{{clock}}` is a `<clock>` block with the date and local time, and
`{{session}}` is a `<session>` block with the session id and approval mode;
each is empty when the host has no value. The built-in Work, Plan, and child
Agent templates place `{{clock}}` and `{{session}}` last, after the
instructions, so the volatile text sits at the end of the system message. The
built-in Review, compaction, and image-description templates declare only
`{{env}}`. `{{model_info}}` is a `<model>` block with the provider and model.
No built-in template declares it and a custom mode never gets it appended, so
the model-bound text names the model only when an operator template writes the
slot. `{{directories}}` is empty when
the session has no added directory rules. Otherwise it is the same session
directory access text appended to workspace instructions. Built-in templates
do not declare this slot. It refreshes when the live system message is
replaced, including after `/access`. Review user templates add `{{conversation}}`,
`{{tool_name}}`, `{{tool_arguments}}`, `{{host_risk}}`, `{{host_authority}}`,
and `{{classification_summary}}`. Extended approval user templates add those
names plus `{{primary_review}}`, the primary verdict as plain lines, or a
short note that it is not shown. Compaction user templates add
`{{conversation}}`, `{{prior_summary_section}}`, `{{summary_task}}`,
`{{output_template}}`, and `{{todos_section}}`. Image user templates add
`{{question}}`. A disk plugin may add further
names through `IPluginPlaceholder`. The host rejects a name it already owns,
and the first loaded plugin keeps a duplicated name. Those values expand in
the same binding pass as the host names, including attachments, Review,
compaction, and image description. A returned value is inserted as text, so tokens inside it stay
literal. A resolver that throws, or returns no text, leaves the token and
records an English note. Built-in Work, Plan, Review, compaction, and
image-description templates declare the slots they need explicitly. Topic
naming uses `topic.md`. It is a named prompt, so a prompt set, Home, or
project file can replace it. The live session does not invoke it yet. Review
system text is the named file plus any enabled review
attachments. It does not receive the workspace instruction block. Its user
turn is the named `review.user` template. Extended approval system text is the
named `review.extended` file, with no attachments, and its user turn is
`review.extended.user`. Both use `{{mode}}` `review`. Composite
and atomic host values are not overlay files and refresh on `/cd`, `/model`,
`/approval`, and when the live system message is replaced. Skill guidance is host-owned
and is not an overlay file.

Prompt set selection and direct prompt overrides are separate features. A
prompt set is a Home-only directory under `~/.crystal/promptsets/<name>` with
`prompt.json` and any subset of the named prompt files (`work.md`,
`plan.md`, `review.md`, `review.user.md`, `review.extended.md`,
`review.extended.user.md`, `topic.md`,
`compaction.system.md`, `compaction.user.md`, `image.system.md`,
`image.user.md`, and `agents/general.md`, `agents/work.md`, and
`agents/plan.md`; `.txt` is also accepted). The host never scans a workspace for prompt sets.
`default` is a virtual, reserved selection backed by the built-in prompts. A
missing or empty file in a selected set falls back to the built-in prompt for
that name. Direct `prompts/work.md` overrides do not use `prompt.json`.

`prompt.json` may set `name` (display title, at most 80 characters) and
`description` (at most 280 characters). The directory name stays the id.
`enabled` is required to turn the directory on; omitting it means off.
Unknown fields are kept. A missing, unreadable, or invalid manifest skips
the directory and reports an English note. Commands do not create a missing
directory or a missing manifest, and they leave a broken manifest unchanged.

At most one prompt set may be enabled. Enabling one writes `enabled: true`
on that manifest and `enabled: false` on every other enabled set. Disabling
the current set, or `/promptset default`, clears every enabled flag and the
session uses built-in text. If two or more sets are hand-edited to
`enabled: true`, the session uses none, notes the conflict, and stays on
`default`.

The complete named-prompt precedence is built-in, selected prompt set, direct
`~/.crystal/prompts` override, then direct workspace `.crystal/prompts`
override. That order covers Work, Plan, Review, the Review user turn, topic
naming, extended approval system and user text, compaction system and user
text, and image-description system and user text. Instructions, skill
guidance, and the environment block are not
prompt-set members. Compaction's required output format stays inside
`{{output_template}}`; replacing `compaction.user.md` can omit that slot.
`/promptset` (alias `/prompts`) lists sets and
the effective source of Work, Plan, and Review. `/promptset <name>` switches at
idle by writing `prompt.json`, replaces the live system message, and rebuilds
Review. It does not write `config.json`. A process `--prompt-set` refuses
that switch and leaves the file unchanged. Resume and fork use the enabled
manifest rather than a selection stored in the session. `crystal promptsets
list|show|enable|disable` edits the same Home manifests outside a session.
`--home` and `--format text` match the plugin commands. There is no
`--source` flag, because prompt sets are Home-only.

Prompt attachments append after the resolved Work, Plan, or Review text has
been bound. The resolved text stays in place. Attachments are not prompt-set
members. An attachment only appends Work, Plan, Review, or one built-in child prompt
(`agents/general`, `agents/work`, `agents/plan`). Topic, Review
user, extended approval, compaction, and image description are replaced by
named prompt files, not by attachments. Each attachment is a
directory of `prompt.json` plus `work.md`, `plan.md`, `review.md`, and the
`agents/` files (`.txt` is also accepted). A missing file contributes nothing for that
mode. Empty files are missing. A trailing newline at the end of an
attachment file is kept. Directory names use the same 1-64 character
hyphenated form as prompt sets. `default` is a valid attachment name.

Discovery reads `~/.crystal/prompt-attachments/<name>/` and
`<workspace>/.crystal/prompt-attachments/<name>/`. The workspace is the
current workspace root only; parent directories are not walked. The same name
in both places uses the workspace `prompt.json` entirely, including `enabled`
and `order`. Many attachments may be enabled. They sort by `order`, then by
directory name. A missing `order` sorts last. Enabling appends the name and
rewrites the enabled orders as `0..n-1`. Disabling sets `enabled` false and
does not renumber. `/promptattach up` and `down` swap within that order and
rewrite it. Placeholder tokens inside an attachment expand with the same host
values as the body.

Composition order is the resolved body, bound placeholders, enabled
attachments for that mode in `order`, then an ordinary plugin `OnPrompt`
append. A raw `RewritePrompt` can still replace the result. `/promptattach`
lists discovered attachments with title, description, and Home or workspace
source. Changes are refused while a turn is running. Listing stays available.
The status bar continues to show only the selected prompt set directory name.
`crystal prompt-attachments list|show|enable|disable` edits the same
manifests. It accepts `--home`, `--workspace`, `--source home|project`, and
`--format text`. Session `/promptattach` edits the winning directory and has
no source prefix.

`crystal`, `crystal space`, and `crystal run` accept `--prompt-set <name>`.
That flag forces the named set for the process even when
its manifest says `enabled: false`. `default` forces the built-in text.
`/promptset` switching, including `default`, is refused while the flag is
set and does not write `prompt.json`. Listing and export stay available.
`--prompt-attachments on|off` is the process switch: omitted or `on` follows
the file flags and order; `off` appends nothing and refuses enable, disable,
up, and down. Listing stays available. Repeatable `--prompt-attachment <name>`
appends those discovered names after the enabled set, even when a manifest
says `enabled: false`. A name that is already enabled stays in its manifest
order and is not appended twice. The same name passed more than once is kept
once, in first-seen order. Combining it with `--prompt-attachments off` is
rejected. A name that is not discovered in Home or the launch workspace fails
startup. `/promptattach enable`, `disable`, `up`, and `down` are refused for a
forced name and do not write `prompt.json`. Other names can still be changed.
Listing stays available and marks a forced disabled row. A later workspace
change that no longer contains a forced name skips that name and prints a
note. These flags do not write
`prompt.json` or `config.json`. Older `promptSet` and `promptAttachments`
keys in `config.json` are ignored and omitted the next time preferences are
saved. There is no migration tool.

## Model

`/model` changes the configured provider and model for the next idle
turn. Crystal `ChatRequest` has no model field; adapters bake the name
and sampling into client options, so the session rebuilds
`IStreamingChatClient`. A compaction model that is on keeps its own
client; otherwise the next summary opens a client for the new session
model and puts the summary reply cap on it. An
approval model that is on keeps its own Review client. The transcript
stays the same conversation.

- `/model` lists catalog models grouped by provider and marks the
  current selection.
- `/model <model>` selects a model on the current provider. If that
  provider does not list the name, a unique catalog match is used. A
  provider name with one model selects that model.
- `/model <provider> <model>` selects across providers. The model id
  is the remainder after the first space and may contain `/`.
- Models that are not listed cannot be selected. A missing API key
  leaves the current client unchanged.
- The command is refused while a turn is running.
- A successful switch writes `provider` and `model` to `config.json`.
  CLI `--provider` / `--model` still override only that process start.
- Thinking follows the existing rule: switching models never fails.
  Unsupported thinking is omitted; an unsupported stored gear uses the
  provider default and is not rewritten.
- Reasoning history follows "switching never fails" too. An adapter
  replays opaque reasoning state only where its protocol and profile
  can carry it. Every other block keeps its readable text, as
  assistant text or the protocol's readable reasoning field (Ollama
  thinking), and drops the state; a block with no readable text is
  omitted.
- Compaction is not run as part of the switch. The next turn uses the
  new context window.

## Session directory access

`CodingSession` owns directory authorization for one session. Its workspace
resolver carries the live `DirectoryAccessPolicy`, separate from the primary
workspace root and approval grants. Immutable `DirectoryAccess` values contain
canonical absolute `path` and `mode` (`read` or `write`), plus optional `sourcePath`
for the absolute, lexically normalized entry path when it differs from the target.
The entry path is revocation metadata only; authorization always matches `path`.
Write includes read.
The implicit workspace is writable; the most-specific directory rule wins,
including an explicit read-only rule for the workspace or one of its children.
Matching uses directory boundaries and Windows case-insensitivity, resolves tool
paths through symlinks, and checks credential paths independently.

`/access` lists the primary workspace and session rules. `/access add <path>
read|write` validates an existing directory and adds or replaces its canonical
rule; `/access remove <path>` first matches the recorded entry path, then the
canonical target, even if the directory or entry symlink no longer exists. A
retargeted entry revokes the original rule, not the new target's independent rule.
Path resolution failures report the path error, not command usage. Paths containing
spaces can be double-quoted. Mutations are refused during a turn
or compaction. Changes immediately save `SessionDocument.access`, refresh the
live model instructions and the `{{directories}}` placeholder, and affect the already-bound executor through the shared
policy. On a new session, any `--allow-read` or `--allow-write` flag makes
those flags the complete seed. They replace workspace defaults, are not merged
with them, and are recorded only as session access metadata, never as
preferences in `config.json` or `access.json`. A resumed session keeps its
saved rules; flags still overlay those rules, and defaults are not loaded.
Conflicting modes for the same canonical root are rejected at launch. When no
flag is set and the session is not resuming, `~/.crystal/access.json` supplies
the seed. That file
is keyed by the canonical workspace root, not the git trust root, and lives
under Home so a project cannot grant itself extra directories. A missing file
seeds nothing. A corrupt file or two modes for one directory seeds nothing and
reports an error; the session still opens. A missing directory, a credential
path, or a bad mode is skipped with a visible error and left in the file.
`/access default` lists the stored rules. `/access default add <path> read|write`
validates like `/access add`, writes the file, and adds the rule to the current
session. `/access default remove <path>` removes the stored rule and the same
canonical rule from the current session only. Other saved sessions are left
alone. Mutations are refused during a turn or compaction.

Built-in file reads in authorized directories use ordinary workspace read
approval. Writes in writable directories follow the current approval mode.
Outside Full mode, `ApprovalPolicy` rejects explicit read-only writes before classification hooks,
grants, or review; write, edit, and apply_patch recheck at path resolution
before file I/O. A patch is rejected whole when any of its paths, including a
move destination, is in a read-only directory.
Full bypasses these directory restrictions through the shared workspace resolver;
changing approval mode restores them without altering saved access rules.
Unlisted outside paths follow the current approval mode. Credential paths
remain forbidden. Relative paths stay relative to the one primary workspace.
Search and enumeration keep their existing root fence and do not automatically
search all authorized directories or traverse cross-root links. The glob, grep,
and search tool descriptions and path schemas explicitly allow workspace-relative
or absolute roots, default to the workspace, and explain session access versus
approval for unlisted outside roots.

Model-bound text describes only what the model should do: stay inside the
workspace and listed directories, leave read-only directories alone, do not
use shell or extensions to get around access, and stop retrying a denied call.
Built-in prompts, tool descriptions, and the session directory access text do
not name approval modes, the Review model, risk classes, or what Full bypasses.
Approval stays a host decision. Work and the `bash` description prefer the
built-in file tools for lookups and changes, and allow bash when those tools
cannot do the job.

Resume restores the saved absolute rules and entry paths, including rules for
missing directories; it never resolves a saved entry to a newly retargeted root.
Invalid saved rules fail closed rather than silently losing read-only restrictions.
`SessionResume.TryLoad` validates access before returning a document, and direct
session construction validates it before allocating session resources. Interactive
startup reports a red error and exits with code 1; in-session resume reports an
error without changing the current session or its rules. Resume does not
reapply workspace defaults, so a default removed during that session stays
removed. Fork does not copy the source session's access. Clear, fork, and a
successful workspace change start from the destination workspace's defaults.
A refused or no-op change does not. Additional roots do not discover
prompts, instructions, plugins, tools, or skills. Existing session files with no
access field load with an empty rule list; rules without `sourcePath` remain valid
and can be removed by their listed canonical target. Shell, background shell, external tools,
and plugins retain their existing approval behavior and are not sandboxed by this
policy. An operating-system sandbox remains deferred.

### File tool results

`edit`, `write`, and `apply_patch` end their result with a numbered excerpt in
the `read` layout (`     12|text`), so the model can check the outcome without
another read. `edit` shows the changed lines with 5 lines of context on each
side; with `replace_all` it shows the first change and the count. `write`
shows the first lines of the file. An excerpt that would be empty, as after
emptying a file, is omitted. Excerpts are model-visible tool text, use LF
separators, and are not stored anywhere else.

`grep` prints `path:line:text` by default. `output_mode` `files` lists each
matching path once and `count` prints `path:count`. `context` (0 to 50) adds
neighboring lines as `path-line-text`, with `--` between separate groups; a
context line that two matches share is printed once. `limit` defaults to 2,000
and is capped at 10,000 matches in `content` mode, or files in the other modes;
a stop at the limit says so. Output is also bounded by the tool output size.

A model entry on a `responses` provider may declare `textTools`. The factory
then gives the adapter a `TextToolBinding` for `apply_patch` and its one
property, `patchText`. The tool stays a JSON tool everywhere in the host:
catalog, disabling, replacement, approval, grants, the transcript, the archive,
and compaction all see `{"patchText": ...}`. Only the Responses adapter changes
the form, and only when the request's own tool definition for that name is a
JSON object with exactly that one string property. Such a tool is sent as
`{"type":"custom","name","description"}` with no schema. A `custom_tool_call`
for it, whole or streamed, is wrapped back into `{"patchText": <text>}` with
the JSON input kind; the stream holds the text until
`response.custom_tool_call_input.done`, falls back to the held deltas when that
event carries none, and emits it once. A call and its result go out as
`custom_tool_call` and `custom_tool_call_output` only when the request offers
the tool as custom and the recorded arguments hold the one string. Otherwise
the pair keeps its recorded `function_call` form, so switching models,
disabling the tool, or a replacement that changed its schema never leaves a
call without a matching result type. A custom call to a name that was not
offered this way stays text input and is refused by name. A tool that can
return an image is never folded, since a custom tool's output is text. The
declaration is refused when the file loads for any protocol but `responses`.

`apply_patch` takes `patchText`: Add File, Update File, and Delete File
sections between `*** Begin Patch` and `*** End Patch`. An Update File section
may carry `*** Move to:` and holds change blocks. A block may start with one or
more `@@ <line>` anchors that must be found, in order, before its lines are
matched. Lines starting with a space are context, `-` removes, `+` adds, and
`*** End of File` ties a block to the end of the file. Matching tries exact
text, then ignoring trailing spaces, then ignoring surrounding spaces, then
ignoring typographic quote, dash, ellipsis, and space differences; the looser
passes run only when the stricter ones find nothing. The patch is read, every
path is resolved and checked, and every Update is computed against the bytes
on disk before the first file is written, so a bad block, a missing file, an
existing Add or Move destination, a credential path, a read-only directory, a
binary or non-UTF-8 file, an oversize result, or a path named in two sections
leaves every file as it was. Sections then apply in order. An Update is written
only if the file still holds the bytes it was computed from. A move writes the
destination before deleting the source. If a write fails part way, the result
names the failing file and the files already changed. Updates keep a byte order
mark, the file's newline style, and whether it ended with a newline.

The patch is classified by its riskiest path, including each move destination:
any credential path makes the call Forbidden, any path outside the session's
directories makes it an outside write, and otherwise it is a workspace write.
A reusable grant for a patch is keyed by its sorted set of paths, not by the
patch body. Plan does not offer it. Edit and Review let a workspace patch pass
without review, like `edit` and `write`.

## Approval

The following mode descriptions use workspace access for built-in file tools to
include session-authorized directories of the appropriate read/write capability.
Explicit read-only writes are rejected outside Full; outside paths mean unlisted paths.

Every side-effect tool call is classified before invocation:

- Risk: Read, Write, Privileged, Forbidden.
- Authority: Workspace, OutsideWorkspace, Network, PrivilegedEscalation.
- Grant: Once, Session, Persistent.

Modes:

- Plan: no built-in edit, write, apply_patch, or bash. Workspace reads auto-execute.
  Reads, list, tree, glob, grep, and search of paths outside the workspace ask the operator.
  When Skills is enabled, any path inside a Skills search directory
  (`skill` / `skills` trees) auto-executes as a workspace read. The
  comparison uses the final target of each symbolic link. External
  tools listed for Plan keep Write + Workspace and still go through
  approval.
- Default: Workspace Read auto-executes. Write, shell, and
  paths outside the workspace ask the operator.
- Edit: workspace file changes for built-in `write`, `edit`, and `apply_patch`
  pass without review. Shell, external tools, and paths outside the
  workspace still ask.
- Review: workspace file changes for built-in `write`, `edit`, and `apply_patch`
  pass without review, same as Edit. Another model checks
  each remaining side-effect call (Codex guardian-style), including
  bash, bash_start, bash_stop, reads, list, tree, glob, grep, search, write, and edit of paths outside the
  workspace, and external Write. Workspace reads and Skills search
  directories still
  auto-execute. A bounded transcript excerpt is attached: the first
  and latest user turns as authorization anchors, other user turns
  that fit, then recent assistant and tool evidence. A compaction
  summary stands in for folded user turns. Without that evidence the
  host asks the operator. Later user messages refine the task; a
  status question does not revoke earlier authorization. Answers the
  user gave through the question tool count as authorization evidence.
  The reviewer
  returns `outcome` (allow / deny / ask), `risk_level` (low / medium
  / high), `user_authorization` (low / medium / high), and
  `rationale`. Allow executes. Deny becomes model-visible rejection
  text. Ask, Forbidden-allow, and forced-prompt allow fall back to the
  operator. A transient review request failure is retried with the
  `approvalRetry` policy (default five retries, same backoff shape as a
  model round); if it still fails the host asks the operator. Review is
  not a grant and is not full pass-through.
- Audit: the same reviewer and transcript rules as Review, but
  workspace `write`, `edit`, and `apply_patch` are also checked. They do not
  auto-pass.
- Full: all tool calls pass without review or operator approval, including
  outside-workspace, Network, Privileged, Forbidden, and forced-prompt calls.
  Session read-only directory rules and execution path fences do not restrict
  Full. Credential paths remain prohibited by file tools. Tool catalogs,
  argument validation, OS permissions, cancellation, and budgets still apply.
  Session and Persistent grants apply only to ordinary Workspace Read/Write
  calls outside Full; they cannot bypass a raised classification.

The reviewing model is the session model unless `approvalModel` is
enabled in `config.json`. The object is `enabled`, `provider`, `model`,
and optional `thinkingEffort`, `serviceTier`, and `speed`. An omitted object means off.
`serviceTier` and `speed` apply to that reviewer only and must be tokens the
reviewer model lists; an enabled reviewer with an unlisted token fails at
startup. An unset slot follows the per-model `tierSelections` and
`speedSelections`. `/approval tier` and `/approval speed` cycle the reviewer
model's list, including the unset slot, or select a listed token, and write
`config.json`. Selecting another reviewer model clears both slots.
`enabled: false` keeps a stored provider, model, and gear unused, and
Review and Audit follow `/model` and the work thinking gear. While
enabled, review calls use a separate client and that provider's
credentials. `/model` and the work thinking gear do not apply. The
reviewer uses the stored gear; a stored gear the model does not offer
falls back to the provider default, like the work gear. Review usage is not added to the work
transcript. A missing credential, a missing catalog model, or a saved
switch that names a model the catalog does not contain fails instead
of falling back to the session model. `/approval model` shows the
switch, turns it on or off, or sets a model or `provider model` and
turns it on; a stored gear is kept. `/approval thinking` cycles the
stored gear or sets one
by name and persists it; it needs a stored or enabled approval model,
but works while the switch is off. `/status` adds
Approval model and Approval thinking rows only while the switch is on.
`/status full` lists the effective retry policies. The status bar does not.
The Approval model row adds the effective tier and speed when either is set.

Extended approval is an optional third model for Review and Audit. It is
not an approval mode, a grant, or the product review in TODO.md. It runs
only after the primary reviewer returns allow on a call that would then
execute. Forbidden and forced-prompt calls already reach the operator, so
they skip it; a primary deny or ask never reaches it. It runs when the
primary `risk_level` is at least the configured trigger (`low`, the
default, means every review allow). It receives the same transcript excerpt
and proposed action as the primary reviewer and, unless turned off, the
primary outcome, risk level, authorization, and rationale. Its verdict uses
the same JSON shape and parser. Allow executes and reports pass reason
`extended review` with both verdicts. Deny returns model-visible text that
starts `The extended approval reviewer declined this action:`. Ask, a request
failure after `approvalRetry`, or unusable output reaches the operator, and
the approval card shows both verdicts. `crystal run` denies that prompt. The
built-in prompt defaults to allow and names the concrete problems that justify
deny or ask: unrequested irreversible destruction, secret exposure, effects on
shared or production systems, unexpected data egress, and signs that
untrusted text steered the call.

`config.json` field `extendedApproval` holds `enabled`, `provider`, `model`,
`thinkingEffort`, `serviceTier`, `speed`, `trigger`, and
`includePrimaryReview` (default true). An omitted object means off, and
default values are omitted on write. An enabled switch needs a provider and
model and never falls back to the session or approval model; a missing
credential, catalog model, or unlisted slot fails. Its client is cached apart
from the work, review, and compaction clients and is rebuilt when its model,
tier, speed, context budget, or output cap changes. Its usage is not added to
the work transcript. Plugin model facts do not describe it.
`/approval extended` shows the switch, `on` or `off` sets it, and a model or
`provider model` selects one and turns it on. `/approval extended thinking`,
`tier`, and `speed` follow the approval-model commands. `/approval extended
trigger low|medium|high` and `/approval extended primary on|off` set the
other fields. Each writes `config.json`. `/status` adds Extended approval and
Extended thinking rows while the switch is on; `/status full` also lists
Extended trigger and Extended primary review.

`crystal run` can set the switch for one process with
`--approval-model on|off`, `--approval-provider`, `--approval-model-id`,
`--approval-thinking`, `--approval-tier`, and `--approval-speed`. Extended
approval has `--extended-approval on|off`, `--extended-approval-provider`,
`--extended-approval-model-id`, `--extended-approval-thinking`,
`--extended-approval-tier`, `--extended-approval-speed`,
`--extended-approval-trigger`, and `--extended-approval-primary on|off`.
Those flags are not written to `config.json`.
Passing a provider or model id turns the switch on for that process.
`--approval-model off` keeps any stored names and gear unused and rejects
a provider or model id on the same command. `--extended-approval off`
follows the same rule.

Legacy config values `autoedit`, `fullreview`, and `full-review` still
parse to Edit and Audit.

External tool authors may set `approval` to `inherit` (default) or
`always` at the set level and override it per tool. The operator controls
whether author declarations are effective independently for Home and Project
tools through `externalToolApproval` in `config.json`. Defaults are Home
`author` and Project `host`: installing under `~/.crystal/tools` trusts the
author declaration, while workspace-authored declarations stay under host
policy unless the operator opts in. An effective `always` skips prompts and
Review for ordinary workspace-bounded calls. Classification, schema checks,
credential-path rejection, path fencing, and other execution safety floors
still apply. Tool updates inherit the declaration; there is no content hash or
version-bound trust grant.

Do not name a mode `auto`. That word is ambiguous between review and
full pass-through.

When a call auto-passes (policy, remembered grant, review allow, or
extended review allow),
the shell prints a panel with Title Case fields: Status, Reason, Risk,
and Authority, plus the classifier summary. A review allow also prints
Outcome, review Risk, Authority, and rationale. An extended review allow
then prints the same fields again under an `Extended review` rule.

Persistent grants are stored in `~/.crystal/permissions.json`. Existing grants
remain readable, but only apply when the current classification is Workspace
Read/Write without `RequirePrompt`. OutsideWorkspace, Network, Privileged,
Forbidden, unknown classifications, and `RequirePrompt` cannot use or create
reusable grants. Their operator allow applies once, regardless of the scope
returned by the front end. Forced-prompt restrictions apply outside Full mode.
`RequirePrompt` also prevents a reviewer Allow from
executing: Deny still rejects, while Allow and Ask reach the operator. The
headless front end denies any such operator prompt.

## Compaction

Crystal does not reduce context. Before each model round, and again
before the post-turn compaction check, optional microcompaction may
replace old successful tool results with `Tool result omitted after
compaction.` It does not call a model. The last `protectTurns` user
turns stay intact, then the newest `protectTokens` of older successful
results stay intact. Older successful results are replaced only when
the estimated size of the removed text is more than `minimumTokens`.
The placeholder that replaces them is not subtracted from that estimate.
User text, assistant text, and
failed results stay. The walk stops at the previous summary. The
archive, the screen, resume, fork, and export keep the original tool
text. A rewrite is saved on the model transcript. The following compact
decision then uses that transcript's estimate, because the previous
provider usage still counts the removed text. Microcompaction is off
when `microCompaction` is omitted. `/compact micro` toggles it, and
`on` or `off` sets it, writes `config.json`, and does not summarize.
It is refused during a turn or compaction. `/status` adds a
Microcompaction row only while it is on. Defaults are 2 turns, 40000
protected tokens, and 20000 minimum tokens. `protectTurns` must be at
least 1. The token counts must be non-negative. Invalid values refuse
startup. Full compaction's own prune does not follow this switch.

Reserved output is `reservedOutputTokens` when set. Otherwise it is
the smallest of 20000, the model `maxTokens`, and a quarter of the
window, so a small window keeps most of itself for input. The
compaction ceiling is the lesser of the configured fraction of the
window and the window minus the reservation. Raising the reservation
lowers that ceiling, and lengthens the reply, only when the reservation
is already the tighter of the two. While the fraction leaves a larger
gap, a bigger reservation changes neither. A model request asks for
at most `min(maxTokens, window - ceiling)`, so the prompt and the reply
fit the window together. A model without `maxTokens` sends no cap and
keeps the provider default. Switching the selected context budget
rebuilds the session client and drops the cached plugin session client,
plugin image client, and image-description client, because each output
cap is fixed when that client is opened. The review client is dropped
when its own budget or output cap changes, including when review uses
the session model.

The size that is compared with that limit is the last finished model
round's provider-reported input plus output, plus a local estimate of the
items added after the final item of that round (normally tool results). The
report is the anchor. It is matched by item identity, so a rewrite of the
transcript, such as microcompaction or compaction, drops the anchor and the
next check estimates the whole list until a round reports again. A round
with no usage from the endpoint sets no anchor. Without an anchor, the whole
list is estimated and the last reported usage is checked beside it. With one,
the anchor already holds that report. A reasoning block counts once: the
larger of its readable text and its provider state, because that state
repeats the text.

When that size crosses
the lesser of the configured fraction of the model context window and
the usable window (context minus reserved output), the host:

1. Clears old tool results outside a protected recent band of 2 user
   turns and 40000 tokens, when that frees more than 20000 tokens.
2. Asks for one structured summary of older turns, folding any
   previous summary, and keeps a recent tail verbatim. The tail is 25%
   of the usable window, at least 2000 tokens and at most 32000. That
   summary request follows the same `retry` policy as a model round.
   The session model writes it unless `compactionModel` is enabled.
   The summary prompt caps its reply at one quarter of the compact
   ceiling, at most `min(writer maxTokens, 16384)`, and never more than
   half the effective summary window, so the result lands below the
   ceiling that would trigger the next compaction. The writer is the
   session model, or the compaction model when that is on. A writer
   without `maxTokens` still sends that cap. The cap is fixed on the
   summary client. Crystal's `ChatRequest` cannot lower one call, and
   the session client keeps the turn cap. A summary whose finish reason
   is `length` is not stored.
3. Stops if the summary request itself cannot fit or the summarizer
   returns nothing and prune did not help. The turn does not retry
   compaction in a loop.

CTX percent uses the last model round's provider usage while a round is
streaming, then a separately marked `~` estimate after each tool batch until
the next round reports. That estimate is the anchored size above, so it adds
only the new tool results to the last report. Estimates never replace saved
provider usage and are not part of cumulative usage. CTX is not the sum of rounds in
the turn. The status bar can show 100% when usage meets or exceeds the
window. Compaction does not block the session loop: the progress row
stays on `Compacting` and the composer remains usable.

`/compact` (alias `/summarize`) runs immediately and does not wait for
the threshold. When the retained tail already holds every turn, it still
summarizes everything before the latest user turn. A session with nothing
before that turn prints `Nothing earlier to compact`. It is refused while
a turn is running. `/compact full` takes no argument. It folds the whole conversation, including
the latest user turn, into the summary and keeps no tail, so the model
context becomes the live system prompt and the summary. It follows the same
display rules as `/compact` and prints `Nothing earlier to compact` when the
session has no conversation. `/compact model` and `/compact thinking` show or
set the optional summarizer and its gear. `/compact summary` toggles
`showCompactionSummary`, and `/compact auto` (alias `automatic`) toggles
`showAutomaticCompactionSummary`. `/compact micro` toggles microcompaction.
Any of those commands accepts `on` or `off`, writes `config.json`, and does
not summarize. Any other argument is rejected.
`compactionModel` in `config.json` is `enabled`, `provider`, `model`,
and optional `thinkingEffort`. An omitted object means off, and
`enabled: false` keeps a stored provider, model, and gear unused.
While off, the summary request uses the session model and omits
reasoning. It does not reuse the session client, because that client
keeps the turn output cap. The host opens a summary client with the
summary reply cap, and opens it again when the model, tier, speed, or
cap changes. While on, the host opens a separate client and maps that
model's gear the same way as the approval model: no thinking omits
reasoning, `off` disables it when the model allows that, and `default`
or an unsupported gear uses the provider default without changing the
saved gear. Changing the session model does not replace that client.
A side question that must summarize first opens its own summary client
and releases it; the answer still uses the session model.
The trigger, retained tail, and CTX percent stay on the session
window. The summary prompt budget is the smaller of that window and
the compaction model's declared window. A summary that cannot fit
takes the existing path that does not write one. `/status` adds a
Compaction model row only while the switch is on.
`crystal`, `crystal space`, and `crystal run` accept
`--compaction-model on|off`, `--compaction-provider`,
`--compaction-model-id`, and `--compaction-thinking` for one process.
Those flags are not written to `config.json`. `on` needs a saved
selection or a provider and model id on the same command.
User and assistant text in the folded head are replaced
by the summary; they are not kept beside it. The screen keeps the earlier
lines. When `showCompactionSummary` is on (the default; omitted from
`config.json` when on, `false` when off), a manual `/compact` prints the
stored summary under those lines in a rounded panel titled `Earlier context`.
Automatic compaction prints `Compacted context` only, unless
`showAutomaticCompactionSummary` is on. That switch defaults off, is omitted
from `config.json` until enabled, and uses the same panel. It does not change
`/compact` itself. A compaction with no summary text does not print one. A side
question that summarizes a private copy does not print it.

Sessions are written to `~/.crystal/sessions/<id>.json` after each
completed turn, after a child invocation settles, after a successful `/compact`,
and on an orderly exit
when the transcript has a user message or a compaction summary. A running
turn is also checkpointed: after its user message is added, after each model
round, and after each tool batch is committed to the archive. A checkpoint
uses the live model context, and a tool call without a result gets the
checkpoint-only result described for child checkpoints; the live turn is not
changed. A turn that ends without a result, including a recalled message, is
saved again with the state the next turn starts from. A checkpoint that cannot
be written prints one note and does not fail the turn. A process that is killed
or loses power therefore loses at most the model round or tool batch in flight.
Child invocations are not checkpointed while they run; the parent checkpoint
records them as unfinished. The
file stores the compacted model transcript (live system prompt, one
summary, recent tail, and reasoning items with their readable text and
opaque provider state), the last usage snapshot, and cumulative provider usage.
Switchable thinking-chain compression is deferred product work. The host
does not, after a thinking pass, ask a model to compress and summarize
that thinking according to a requirement. Context compaction,
microcompaction, `verboseThinking`, and Anthropic `thinkingDisplay`
`summarized` are not that path. No prompt file, configuration field, or
command is reserved for it.
`crystal --resume` (`-r`) opens a terminal selector for this workspace.
`crystal --resume <id>` loads that file and stays in the process workspace.
`crystal --resume <path>` lists that directory, asks for trust before the
selector, and enters the directory after a session is chosen. Escape leaves
the process workspace unchanged. `crystal --resume all` lists every workspace,
shows each path, and enters the chosen session's workspace after trust.
`--workspace` and `--resume <path>` must name the same directory when both
are set. A missing or empty session exits without entering the TTY.
`/resume`, `/resume <path>`, and `/resume all` use the same rules inside a
running session. `/resume <id>` restores that transcript and stays in the
current workspace. The word `all` is reserved. A value that is both an
existing directory and a session id is rejected. After a workspace change,
the live system prompt is refreshed from that directory's Plan, Work, or custom-mode text;
the summary and tail are kept. The selector lists resumable sessions newest
first by update time, supports typing to filter by id, preview, or workspace
path, and leaves the current session untouched on Escape. Usage is restored
so the status bar, `/status`, and the next compact decision
have a baseline. Sessions saved before cumulative usage was introduced retain
an unknown cumulative value rather than treating their last request as the
whole session. `/clear` starts a new id.

`/sessions` lists resumable sessions for the current workspace in descending
update order; `/sessions all` includes every workspace. The current id is
marked and every row keeps the complete id for use with `/resume <id>` or
`/fork <id>`. Empty and unreadable session files are omitted.

`/fork` first persists the current conversation, copies its transcript,
compaction summary, todos, mode, counters, and usage baseline to a new id, and
continues on that id. The source snapshot remains available under its original
id and is not overwritten by the branch. `/fork <id>` branches a saved session
instead. A branch uses the current workspace and a refreshed live system
prompt. Forking stops a running turn before taking the snapshot; listing
sessions does not.

## Home directory

Self-contained release archives are named
`CrystalCode-<operating-system>-<architecture>.zip`. The supported values
are `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`,
`macos-x64`, `macos-arm64`, `windows-x64`, and `windows-arm64`. The `musl`
variants target Alpine and are built and tested in the Alpine .NET SDK image
because GitHub provides no musl runners. The shell installer selects a musl
asset when it finds `/etc/alpine-release` or a musl `ldd`. The platform
installers replace the full platform release contents under `binaries/code/`.
Developers install the current checkout with `scripts/install-local.sh` or
`scripts/install-local.ps1`. Those scripts publish Release into `build/`
using the release workflow arguments (`--self-contained true`,
`PublishSingleFile=true`, and the matching runtime), then replace the full
contents of `binaries/code/`.

```text
~/.crystal/
  binaries/code/CrystalCode (CrystalCode.exe on Windows)
  config.json
  providers.json
  providers/*.json
  providers/<category>/*.json
  prompt-history.jsonl
  credentials.json
  permissions.json
  trusted.json
  instructions.md
  prompts/work.md
  prompts/plan.md
  prompts/review.md
  prompts/review.user.md
  prompts/review.extended.md
  prompts/review.extended.user.md
  prompts/topic.md
  prompts/compaction.system.md
  prompts/compaction.user.md
  prompts/image.system.md
  prompts/image.user.md
  prompts/agents/general.md
  prompts/agents/work.md
  prompts/agents/plan.md
  promptsets/<name>/prompt.json
  promptsets/<name>/work.md
  promptsets/<name>/plan.md
  promptsets/<name>/review.md
  promptsets/<name>/review.user.md
  promptsets/<name>/review.extended.md
  promptsets/<name>/review.extended.user.md
  promptsets/<name>/topic.md
  promptsets/<name>/compaction.system.md
  promptsets/<name>/compaction.user.md
  promptsets/<name>/image.system.md
  promptsets/<name>/image.user.md
  promptsets/<name>/agents/general.md
  promptsets/<name>/agents/work.md
  promptsets/<name>/agents/plan.md
  prompt-attachments/<name>/prompt.json
  prompt-attachments/<name>/work.md
  prompt-attachments/<name>/plan.md
  prompt-attachments/<name>/review.md
  prompt-attachments/<name>/agents/general.md
  prompt-attachments/<name>/agents/work.md
  prompt-attachments/<name>/agents/plan.md
  skill/<name>/SKILL.md
  skills/<name>/SKILL.md
  modes/<name>/mode.json
  modes/<name>/prompt.md
  tools/<directory>/tools.json
  data/tools/<directory>/
  mcp.json
  mcp/<name>/server.json
  plugins/
  data/plugins/<directory>/
  sessions/<id>.json
  media/<sha256>
  logs/
  server/<pid>.json   (crystal server discovery file, owner-only)
  space/
```

Every Home file the host replaces (`config.json`, `credentials.json`,
`permissions.json`, `trusted.json`, `access.json`, sessions, media, prompt
history, and the `enabled` or model fields it edits in manifests) is written
through `AtomicFile`: a uniquely named dot-prefixed `.tmp` sibling, flushed
with `FileStream.Flush(flushToDisk: true)`, then renamed over the target. A
crash or power loss leaves the old or the new file, never a truncated one.
Owner-only files are created with that mode; other files keep the mode of the
file they replace. Operator configuration files follow a symbolic link and
replace its target; sessions, media, and manifests replace the link itself.
.NET has no managed directory fsync, so a rename that has not reached the
disk at power loss leaves the previous file. Workspace files written by
`write`, `edit`, and `apply_patch` are not covered.

`config.json` stores mutable operator preferences such as the selected provider
and model. `workspaceTrust` asks before the first interactive entry into a
directory (default on; omitted when on, `false` when off). A yes is stored
in `trusted.json` as the git root, or the workspace path when there is no
git repository. That file is not written by the workspace. The operator
space `{home}/space` is trusted without a prompt and without a ledger
entry. Its trust root is that directory even when a parent is a git
repository, so opening it does not grant the parent. `/trust forget` does
not remove that trust or a separate parent grant. A child of `space`
follows the ordinary rule unless `space` is the git root. `crystal space`
creates the directory and opens the terminal there. `/space` switches an
open session to that directory and creates it when it is missing.
`/cd` with a path and `/space` are refused while a turn is running.
`crystal run --space`
uses that directory and creates it when it is missing. It cannot be
combined with `--workspace`. `crystal run`
does not read `workspaceTrust`. `--workspace-trust on|off` is process-only:
omitted or `on` denies an untrusted directory before the session exists,
and `off` skips the check without recording trust. The operator space
passes that check.

For example, an unlimited time budget with finite call limits is:

```json
{
  "executionBudget": {
    "maximumModelCalls": 48,
    "maximumToolCalls": 96,
    "maximumDurationSeconds": null
  },
  "bashTimeoutSeconds": "unlimited"
}
```

`bashTimeoutSeconds` is omitted when it is the 600 second default and
`backgroundBashTimeoutSeconds` when it is the 10800 second default. `null` and
`"unlimited"` both mean no timer. The two timers are independent of each other.

The provider catalog is stored as one or more files. Each file is a root
object keyed by provider name, with an array for multiple protocols under one
name. `ProviderFiles` (`Home`) reads, in order, `providers.json`, the `.json`
files directly under `providers/`, and the `.json` files directly under each
first-level subdirectory of `providers/`, each level sorted by ordinal file or
directory name. Directories below a category and files that are not `.json`
are not read. A subdirectory name only groups files on disk. It must satisfy
`ExternalToolNames.IsDirectoryName` (1-64 characters, a leading letter, then
letters, digits, `.`, `_`, or `-`), and an invalid name stops startup with an
error that names it. The name is not a provider name, does not appear in the
catalog or the UI, and has no effect on merging. A provider name may be
defined in only one file. A repeat in another file stops startup with an error
that names both files; several protocols for one name go in an array inside
one file. A malformed or invalid file stops startup with an error that names
the file. Provider files are Home-only. No workspace path is read.

The catalog overlays built-in definitions. When any provider file exists, the
files supply the operator catalog and the legacy `config.json.providers` field
is not read; otherwise the legacy field is read. Saving preferences copies
legacy provider definitions to `providers.json` only when no provider file
exists, then preserves the legacy field in `config.json` so an older running
version can continue reading it. Preference saves never rewrite a provider
file. Every provider file accepts `//` and `/* */` comments and trailing
commas (JSONC); the host never rewrites them, so comments survive.
`config.json`, including its legacy `providers` field, and every other JSON
file remain strict JSON. Duplicate model ids across protocols under one
provider are rejected. Editing provider definitions in the TUI is deferred
product work. The catalog changes by editing these files, then restarting.

Project overlay (wins over home for named prompts, prompt attachments,
Crystal skills, and tool sets of the same directory name):

```text
<workspace>/.crystal/
  instructions.md
  prompts/work.md
  prompts/plan.md
  prompts/review.md
  prompts/review.user.md
  prompts/review.extended.md
  prompts/review.extended.user.md
  prompts/topic.md
  prompts/compaction.system.md
  prompts/compaction.user.md
  prompts/image.system.md
  prompts/image.user.md
  prompts/agents/general.md
  prompts/agents/work.md
  prompts/agents/plan.md
  prompt-attachments/<name>/prompt.json
  prompt-attachments/<name>/work.md
  prompt-attachments/<name>/plan.md
  prompt-attachments/<name>/review.md
  prompt-attachments/<name>/agents/general.md
  prompt-attachments/<name>/agents/work.md
  prompt-attachments/<name>/agents/plan.md
  skill/<name>/SKILL.md
  skills/<name>/SKILL.md
  modes/<name>/mode.json
  modes/<name>/prompt.md
  tools/<directory>/tools.json
  plugins/<directory>/plugin.json
  data/tools/<directory>/
  data/plugins/<directory>/
<workspace>/.crystal.md
```

`config.json` is not part of the project overlay. Operator preferences load
from the Home `config.json` only; workspace-level configuration is deferred
product work.

Operator plugins and external tool sets keep their own runtime data under a
`data/` root, split by kind. The global root is `{home}/data`; the project root
is `<workspace>/.crystal/data`. Each extension's global directory is
`{home}/data/{plugins|tools}/<directory>` and its project directory is
`<workspace>/.crystal/data/{plugins|tools}/<directory>`. Both are separate from
the `tools/` and `plugins/` install trees. The host resolves both paths and
creates the directories when the extension first receives them, at plugin
attach or before a tool call; it never writes secrets there. A
dotnet host tool reads them from `ToolHostContext.GlobalDataDirectory` and
`ToolHostContext.ProjectDataDirectory`. An exec child reads them as the
per-process environment variables `CRYSTAL_TOOL_DATA` (global) and
`CRYSTAL_PROJECT_DATA` (project). A disk plugin that implements
`CrystalCode.Plugins.Data.IPluginDataDirectory` receives them through
`AttachDataDirectories`.

Overlay is built-in default, then `~/.crystal`, then the project
`.crystal`. A selected prompt set sits between the built-in text and the
direct Home files. Named prompt files replace Work, Plan, Review, the Review
user turn, topic naming, compaction, and image description.

Built-in Work and Plan identify the assistant as Crystal Code. Operators
who replace those files choose their own identity.

`instructions.md` and `.crystal.md` are appended under
"Workspace instructions" on Work and Plan only. Review does not receive
that instruction block. Enabled prompt attachments can still append to
Review. Files may be `.md`
or `.txt`. Empty files are treated as missing. The host never writes
prompt files. The host appends a non-overlayable `<env>` block between
the Work or Plan body and those instructions. When Skills is enabled,
available-skill guidance is appended after the env block. The `<clock>`
and `<session>` blocks follow the instructions.

Skills are OpenCode-compatible `SKILL.md` folders. They are loaded
on demand through the `skill` tool. Available-skill guidance lists
name and description only; it does not include absolute paths. The guidance,
Work, and Plan tell the model to load a matching skill before starting the work
and to follow it, ranked below the user and workspace instructions. When
Skills is enabled, `read`, `list`, `tree`, `glob`, `grep`, and `search` of any path inside a Skills
search directory (`skill` / `skills` trees, including files that are
not `SKILL.md`) auto-execute as workspace reads. The comparison uses
the final target of each symbolic link. Other
outside-workspace reads ask the operator, or go to the Review model
in Review or Audit. They never replace Work, Plan, or Review. `config.json` field `skills`
enables or disables the feature
(default `true`). When `false`, the tool is omitted and skill guidance
is not appended. Later sources overwrite earlier ones with the same
skill name.

- Global: `~/.claude/skills/<name>/SKILL.md`,
  `~/.agents/skills/<name>/SKILL.md`,
  `~/.config/opencode/{skill,skills}/<name>/SKILL.md`
  (`XDG_CONFIG_HOME` is honored), `~/.opencode/{skill,skills}/<name>/SKILL.md`,
  then `~/.crystal/{skill,skills}/<name>/SKILL.md`.
- Project: walk from the workspace up to the git root. At each
  directory, scan `.claude/skills`, `.agents/skills`,
  `.opencode/{skill,skills}`, and `.crystal/{skill,skills}`. Crystal
  paths overwrite OpenCode-compatible paths of the same name.

Each `SKILL.md` needs YAML frontmatter with `name` and `description`.
The skill id is the containing directory name when that name is 1–64
characters of lowercase alphanumerics with single hyphens. Frontmatter
`name` may be that id or a display title and does not have to match
the directory. If the directory name is not a valid id, a valid
frontmatter `name` is used instead. Folded (`>`) and literal (`|`)
YAML descriptions are accepted.

`AGENTS.md` and `CLAUDE.md` are OpenCode-compatible rule files, not
prompt overlays. They are combined into the same instruction block
and never replace Work, Plan, or Review.

- Global: first existing file among `~/.crystal/AGENTS.md`,
  `~/.crystal/CLAUDE.md`, `~/.config/opencode/AGENTS.md`, and
  `~/.claude/CLAUDE.md`.
- Project: walk from the git root down to the workspace. Each directory
  contributes at most one file, the first readable of `AGENTS.md`,
  `CLAUDE.md`, then `CONTEXT.md`. Files are appended from the outermost
  directory inward, so the closest file is the most specific and comes
  last. The combined rule files stop at 32768 characters; a file that
  does not fit is cut at a line break and a note is appended.

`config.json` field `ruleFiles` is an object with `global` and `project`.
Each defaults to on. A missing object or member means on. The object is
omitted when both are on; a written object includes only members that are
off. `global` false skips the four global candidates. `project` false skips
the walk. `/rules` shows the switches, sets both with `on` or `off`, or sets
one side with `global` or `project` followed by `on` or `off`, and writes
`config.json`. It reloads Work and Plan instructions for the next prompt and
does not change an archived turn. It does not change `instructions.md` or
`.crystal.md`. A process `--rules` value is `on`, `off`, or a comma-separated
list of `global=on|off` and `project=on|off`. An assignment leaves the other
side as already configured. The flag is not written to `config.json`. A later
preference save applies only the side that command sets, so a launch
assignment on the other side stays off disk.

`credentials.json` is the persistent API-key store. Values are plain text,
keyed by provider name, and the file is created with owner-only
permissions. Process environment variables override those file credentials.

Operator tool sets live under `tools/`. A project directory of the same
name replaces the home set as a whole. `tools.json` field `enabled`
(default `true`) omits a set without deleting it. `config.json` field
`externalTools` enables discovery (default `true`). Each set also gets
runtime-data directories under `data/tools/`. See
[docs/external-tools.md](docs/external-tools.md).

MCP servers are configured separately from tool sets. Home reads `mcp.json`
and `mcp/<name>/server.json`; a workspace reads the same names under
`.crystal/`. A project server of the same name replaces the home server as a
whole, even when the project entry is invalid. Inside one scope, a name in both
`mcp.json` and a `server.json` directory is ambiguous and neither is used.
`config.json` field `mcp` enables connections (default `true`). The engine
owns connections in `CrystalCode.Engine/Mcp`; a front end only sees notes, the
`/mcp` status, and the slash menu. The engine lends each connection three
things and nothing else: the workspace root for roots, the front end's
`IUserPrompt` for elicitation, and a callback that records a changed tool list.
A recorded change is applied after the turn, never during one, so tools do not
change under a running model. Server prompts are slash commands and resources
are two host tools; neither reaches a front end except through events and
tools. See [docs/mcp.md](docs/mcp.md).
An unreadable or invalid project manifest with the same directory name
also hides the home set. `timeoutSeconds` defaults to 120 for both runners,
independent of `bashTimeoutSeconds`;
`"unlimited"` disables the per-call timer while preserving turn and user
cancellation. Exec processes are killed on cancellation or timeout. Dotnet
calls receive a cancellation token and stop waiting on timeout; in-process
code that ignores cancellation cannot be forcibly stopped.

`/tools` groups the effective host and external catalogs, aligns their Plan and
Work membership, and shows external source, set, author declaration, effective
approval, and catalog totals. It also owns external-tool
configuration: `on`, `off`, `reload`, and independent `home` / `project`
`author` or `host` policies. Changes are persisted to `config.json` and reload
the catalogs when required.

`/status` is the compact diagnostic view. Separate Workspace, Model, Tokens,
and Options cards report operating modes, provider identity, cumulative
provider-reported input/output/total token usage, the latest request's
proportional context bar, and feature switches. `/status full` also shows the
effective `retry` and `approvalRetry` policies, the latest request token
breakdown, and adds Activity and Tools cards for session identity and start
time, invocation counters, queue and todo counts, and tool-catalog totals. The
persistent chrome remains the smallest live summary.
`/status` labels cumulative values as session usage and reserves the latest
request breakdown for `/status full`; its output does not depend on status-line
customization.

Operator plugins live under `plugins/`. A project directory of the same
name replaces the home plugin as a whole. `plugin.json` field `enabled`
(default `true`) omits a plugin without deleting it. `config.json` field
`plugins` enables discovery (default `true`). Each plugin also gets
runtime-data directories under `data/plugins/`. See
[docs/plugins.md](docs/plugins.md).
Dotnet tool sets load class
libraries from the set directory only, in one `AssemblyLoadContext` per
set.

Provider names are open. `deepseek`, `openai`, `gemini`, and `ollama` are starter entries. A user
adds an endpoint by inserting another `providers` object with `protocol`
`deepseek`, `openai`, `responses`, `anthropic`, `gemini`, or `ollama`, a `baseUri`, and a `models`
table. A gateway with different wire formats uses one provider name whose value
is an array of endpoint objects, one per protocol. Model ids must be unique
across that array; duplicates fail configuration loading. A selected model
determines the endpoint, protocol, and credentials used by the adapter.
Repeated JSON provider keys are rejected; the array form preserves both
endpoints. Repeated model keys within one endpoint are also rejected.
Context size and sampling live on each model, not
on the host. Thinking capability also lives on the model. The current thinking
gear is a host setting. An endpoint may also carry an `options` object of
JSON scalars. The object is not a fixed field list: names are read from the
JSON and interpreted by `protocol`. Unknown names fail loading. The only
recognized name is Anthropic `thinkingDisplay`, `summarized` (the default,
not written back) or `omitted` (do not send `display`). Sampling parameters
do not belong there. The API key for `openrouter` belongs in
`credentials.json` under that provider name.

```json
{
  "provider": "openrouter",
  "model": "anthropic/claude-sonnet-4",
  "thinkingEffort": "high",
  "skills": true,
  "providers": {
    "openrouter": {
      "protocol": "openai",
      "baseUri": "https://openrouter.ai/api/v1/",
      "replayReasoningContent": true,
      "tokenLimit": "max_tokens",
      "models": {
        "anthropic/claude-sonnet-4": {
          "contextWindow": 200000,
          "temperature": 0.2,
          "maxTokens": 8192,
          "thinking": true,
          "thinkingEfforts": ["low", "medium", "high"]
        }
      }
    }
  }
}
```

For a mixed-protocol gateway, `providers.openrouter` may instead be an array:

```json
{
  "providers": {
    "openrouter": [
      {
        "protocol": "anthropic",
        "baseUri": "https://gateway.example.test/anthropic/",
        "models": { "claude-model": { "contextWindow": 200000 } }
      },
      {
        "protocol": "openai",
        "baseUri": "https://gateway.example.test/openai/",
        "models": { "gpt-model": { "contextWindow": 128000 } }
      }
    ]
  }
}
```

`thinking` and `thinkingEfforts` declare whether the model supports
thinking and which effort tokens it accepts. A token starts with a
letter and then uses only letters, digits, underscores, and hyphens.
The host stores every token in lowercase and does not interpret it.
`max` and `maximum` are different tokens. A case-sensitive wire value
cannot be spelled here. An empty `thinkingEfforts` list is on/off only. Built-in
DeepSeek V4 models enable thinking with `low`, `high`, and `max`. `thinkingCanDisable` defaults to true. Set it to
false when the model rejects a request to turn thinking off. Built-in
Gemini 3 models do. `/thinking` then omits Off. A stored Off gear falls
back to the provider default and the status bar shows `Think Default`.
An explicit `/thinking off` is refused.

`serviceTiers` and `speeds` declare the tokens a model accepts for service
tier and speed. A token starts with a letter and then uses only letters,
digits, underscores, and hyphens. The host stores every token in
lowercase and does not interpret it. `default` is an ordinary token when
the model lists it. An
unset slot is omitted from the request. `/tier` and `/speed` (alias
`/fast`) cycle the current model's list, including the unset slot, or
select a listed token. Each choice is stored per model in
`tierSelections` and `speedSelections`. Switching is refused during a turn
or compaction. An empty list means that slot is not offered.

Chat Completions writes whichever one slot is set to `service_tier` and
rejects both when the client is created, because that protocol has one
field. The choice is not saved. OpenAI Responses does the same. Anthropic
Messages writes the service tier to `service_tier` and the speed to
`speed`. DeepSeek, Gemini, and Ollama reject either slot when the client
is created. A saved combination the protocol cannot send fails at startup.
The host does not read the
response back to see which value was applied, and it does not infer a
vendor from the endpoint address.

`contextWindow` remains the model's maximum context size. An optional
`contextWindows` array declares selectable local context budgets, for example
`"contextWindow": 1000000, "contextWindows": [272000, 1000000]`.
Values must be distinct positive integers no greater than `contextWindow`;
the host sorts them in ascending order. Without a nonempty array the sole
budget is `contextWindow`. The initial budget is the lowest declared value.
`/context` cycles the current model's budgets; `/context 272k`, `/context 1m`,
and exact integer token counts select one explicitly. Invalid or unavailable
values are refused. Switching is refused during a turn or compaction.
`crystal`, `crystal space`, and `crystal run` accept `--context` for the
model selected at launch when that model declares more than one budget.
The text is the same as `/context`, and it must name a declared budget.
A model with one budget is rejected. The value changes only the live
selection for that model and is not written to `contextSelections`.
Switching models restores the other model's saved budget. A later
`/context` still writes.
`config.json.contextSelections` remembers choices without rewriting provider
definitions. The stored form is a nested object,
`{"provider": {"model": tokens}}`; the older flat `{"provider/model": tokens}`
map is still read and is written back nested on the next save. Values must be
positive token counts, provider keys are normalized to lowercase, and model
ids stay byte-exact, so a model id that contains `/` is stored as written.
A removed or unsupported saved choice falls back to the lowest declared
budget. Switching models restores that model's choice. Compaction, overflow
checks, usage events, `/status`, and CTX percentages use the selected budget.
Lowering it keeps the archive and allows
the existing pre-request compaction check to summarize before the next model
round. These budgets do not send provider-specific long-context parameters
and do not guarantee a billing threshold against token estimation errors or
large tool results.

`thinkingEffort` is the operator choice: `default`, `off` (`none` is
the same), or an effort token listed for that model. It is not stored on the model. `/thinking` (alias
`/think`) cycles the gear or sets one by name. `/model` never
fails because of thinking: if the model does not support thinking, requests omit reasoning
hints; if the stored gear is not in that model's list, the request
uses the provider default and the stored choice is unchanged.
`crystal run` prints that fallback on its error stream and continues.

Each adapter forwards an effort token unchanged. `openai` and
`responses` are also the Chat Completions and Responses paths for
third-party gateways, so they do not rename `maximum` to `xhigh`.
List the spelling that endpoint accepts.

| Protocol | Effort token |
| :--- | :--- |
| `anthropic` | unchanged. Adaptive or budgeted thinking also sets `display` to `summarized`, unless the endpoint sets `options.thinkingDisplay` to `omitted` |
| `responses` | unchanged |
| `openai` | unchanged |
| `deepseek` | unchanged |
| `ollama` | unchanged |
| Gemini 3 | uppercased `thinkingLevel` |
| Gemini 2.5 | `minimal`, `low`, `medium`, and `high` map to fixed budgets. Any other token, including `maximum`, is rejected because the field is an integer |

`skills` enables the `skill` tool and available-skill guidance
(default `true`). Set it to `false` to disable skill discovery.

`ruleFiles` enables OpenCode-compatible rule files. `global` and `project`
each default to `true`. See the instruction discovery section.

`externalTools` enables operator tool set discovery (default `true`).
Set it to `false` to skip `tools/` manifests.

`plugins` enables operator plugin discovery (default `true`).
Set it to `false` to skip `plugins/` manifests. See
[docs/plugins.md](docs/plugins.md).

`mcp` enables MCP server connections (default `true`). Set it to `false` to
connect nothing. `mcpToolApproval` selects, independently for the Home and
Project sources, whether a server's own read-only declaration lowers a tool
call to a read. Each member is `author` or `host`; both default to `host`,
which reviews every call. The object is omitted from the written file when both
members are the defaults; otherwise only changed members are written.

`externalToolApproval` selects whether author `approval` declarations
in `tools.json` take effect, independently for the Home and Project
sources. Each member is `author` or `host`; defaults are Home `author`
and Project `host`. The object is omitted from the written file when
both members are the defaults; otherwise only changed members are
written.

`promptSet` and `promptAttachments` are no longer preferences. A file that
still contains them is read, the values are ignored, and the next preference
save omits both keys. Selection lives in each directory's `prompt.json`.

`estimatedTokens` shows a live four-characters-per-token estimate on
the progress row during Thinking and Writing (default `false`). The
label is prefixed with `~` so it is not mistaken for provider usage.
`/tokens` toggles it, or sets `on` / `off`. A successful change writes
`estimatedTokens` to `config.json` (`true` when on; omitted when off).

`protocol` is `deepseek`, `openai`, `responses`, `anthropic`, `gemini`, or `ollama`. Models that are not listed cannot be
selected. Automatically fetching model-list information from a provider is
medium-priority product work and is not in this build. There is no global
context window.

A persistent key belongs in `credentials.json` as plain text, keyed by
provider name. `apiKey` on a provider definition may be a literal secret,
`{env:NAME}`, or `{file:path}` (relative to `~/.crystal` or absolute, with
`~` expanded). Process environment variables override both the definition
and the file. That override is for one launch or a CI runner.
`requiresApiKey` defaults to `true`, except for the native `ollama` protocol.
When false, a missing key resolves to empty text and the adapter omits its
authentication header. Provider-specific environment variables and explicit
credential references still take precedence; the shared `CRYSTAL_API_KEY`
is not applied to keyless endpoints.

Amazon Bedrock has no dedicated adapter in this build. Its OpenAI-compatible
Chat Completions endpoint can use the `openai` protocol with a Bedrock API key
and the regional `/openai/v1/` base URI for models that support that API.
IAM SigV4 authentication would require a separate signing implementation;
it is not provided by the current bearer-token transport.

## Engine contract

CrystalCode.Engine runs the conversation and reports what happens. A front end
hands it a `SessionFrontEnd`, calls it, and observes events. It never reads
engine state directly and the engine never draws, reads keys, or writes to the
console.

A front end supplies (`SessionFrontEnd`):

- `Observer` (`ISessionObserver`) receives every `SessionEvent`.
- `Approvals` (`IApprovalPrompt`) asks the operator to approve a tool call and
  is told when a call auto-passes or is under review.
- `Questions` (`IUserPrompt`) answers the built-in `question` tool.
- `Sessions` (`ISessionChooser`) picks a saved session for `/resume`, including
  one workspace or every workspace.
- `Trust` (`IWorkspaceTrustPrompt`) asks whether to trust a directory.
  A front end that cannot ask returns false.
- Optionally `ChooseChildAsync` for `/child` and `McpServers`, the MCP
  servers it passes in for this session only.

`CodingSession` wraps `Approvals` and `Questions` once, at construction, in
`NotifyingApprovalPrompt` and `NotifyingUserPrompt`. Every engine path that
asks the operator, including child approvals behind the operator gate and MCP
elicitation, uses the wrapped prompts, so the `operatorNotify` command runs
for the terminal, `crystal server`, and children alike without front-end
code. The notifier starts the command on the thread pool after
`delaySeconds`, and the wrapper cancels the pending start when the prompt
returns. A started command runs to exit or `timeoutSeconds`, with no shell,
the workspace as working directory, and `CRYSTAL_NOTIFY_*` variables plus the
same values as JSON on stdin. Output is drained and discarded. The first
failure in a session is one English note. `CloseAsync` stops pending and
running commands. The `turn` event is raised from `CompleteTurnAsync` when no
follow-up started and the turn was not interrupted, and the next submission
cancels it. Trust, `/resume`, and child choices are operator-initiated and do
not notify. `crystal run` replaces the setting with off in memory.

A front end prepares a session with `SessionLauncher.PrepareAsync`. It maps
its own command line onto `SessionLaunchOptions` and passes a
`SessionLaunchRequest` (working directory, workspace, operator space, resume
target, trust check, whether the launch provider and model may be saved
by a later preference command, and optional additional roots). The launcher
applies overrides in memory,
checks credentials, resolves the resume target, checks trust, and validates
prompt attachments, directory rules, and the mode. It asks the operator only
through the `IWorkspaceTrustPrompt` and `ISessionChooser` it receives; a null
prompt returns `TrustNeedsOperator` or `ResumeNeedsOperator` so the front end
chooses its wording. `SessionTrustCheck.Prompt` follows `workspaceTrust` and
remembers a yes, `Require` refuses an untrusted directory without asking
(`crystal run`), and `Skip` neither checks nor records. The result's
`SessionLaunch.CreateSession` builds the `CodingSession`. The terminal opens
its trust and resume prompts on their own alternate screen
(`StartupPrompt`) before the session frame exists.

A front end calls `CodingSession`: `Create`, `StartAsync`, `SubmitAsync`
(slash commands, follow-ups while a turn runs, interrupts, and turn starts;
returns true when the operator asked to quit), `CompleteTurnAsync` once
`TurnTask` finishes, `FinishTurnAsync` to collect a turn before exit,
`TryInterrupt`, `TryRecall`, `TogglePlan`, `CycleMode`, `TrySetMode`,
`SetVerbose`, `NotifyDraftChanged`, `Enqueue`, `EnqueueAsync`,
`SubmitQuickPromptAsync`, `PasteClipboardImageAsync`, `TryAttachImage`,
`TryAttachImageFile`, `TryGetImage`, `TryExport`, `TryCancelSideQuestion`,
`ClearSideQuestions`, `TrySetDirectoryAccess`, and `CloseAsync`. `TurnActive`, `TurnTask`,
`RecallPending`, `AgentsEnabled`, `PlanMode`, `CustomMode`, and `SessionId`
report state. The usual turn is: `SubmitAsync`, await `TurnTask`, then
`CompleteTurnAsync`.

`SessionRunner` is that loop for a front end without a frame loop of its own.
It is the only caller of the session's input methods while it runs. Inputs
(`SubmitAsync`, `SubmitQuickPromptAsync`) go through one channel in order.
Input that arrives while a session call is running goes to `EnqueueAsync`, or
to a queued quick prompt, as the terminal pump forwards keys. A finished turn
is collected with `CompleteTurnAsync`, which starts a queued follow-up.
`InvokeAsync` runs a synchronous operation (a mode change, a draft update, an
image attach, an export) in order with the inputs, also while a call is
running. Text that was queued while a command ran, with no turn to collect, is
started once that command returns. Cancellation interrupts and collects the
turn without starting queued work. `crystal run` and `crystal server` use it;
the terminal keeps its own pump. `TryInterrupt`, `TryRecall`, and
`TryCancelSideQuestion` stay direct calls because Ctrl+C makes them from any
thread.

`SessionEvent.Origin` names the client whose request produced an event. A
front end serving several clients wraps each request in
`CodingSession.BeginOrigin`; `SessionRunner` does this per input. The turn
task and child invocations start under no origin, so their events have none,
and the engine never sets an origin for a single-operator front end. Draft
images from a paste or `TryAttachImage`, pending `/attach` images, and the
recall window belong to the origin that made them: `NotifyDraftChanged`
discards only that origin's draft images, a submission takes only that
origin's pending images, and `UserMessageRecalled` carries the sending
origin. Closing the session discards every origin's drafts.

`SessionState` is the front-end-neutral reducer. A front end that must redraw
at any time applies every event and reads `SessionSnapshot`: chrome,
preferences, slash commands, prompt history, the committed conversation, the
round still streaming (`LiveRound`, without opaque reasoning state), queue,
todos, status fields, activity, usage, the side-question thread, children
with live usage and todos folded in, and the last turn. Reports addressed to
one client are not session state. `ChildSessionFold` holds the child folding
the terminal's child projection also uses.

`TryAttachImage` attaches bytes a front end received (an upload, or a paste on
another machine) to the draft with the same signature and size checks as a
clipboard paste, and returns the image with its trusted marker. `TryGetImage`
returns a held session image by number. `TryExport` renders the Markdown or
JSON export in memory as `SessionExportContent`; `/export` still writes a
file. `CloseAsync` returns a `SessionClosure` with the session id, whether it
was saved, and the plain-text resume hint the terminal prints. Without a
`ChooseChildAsync`, `/child` reports that child inspection is not available in
this front end.

Events are immutable sealed records, one type per file under `Events`:

- Lifecycle and chrome: `SessionStarted`, `ChromeChanged`,
  `PreferencesChanged`, `SessionOptionsChanged`, `SlashCommandsChanged`,
  `PromptHistoryLoaded`, `ImageHistoryInvalidated`, `ActivityChanged`,
  `LoadingProgressed`.
- Turn: `UserMessageSent`, `TurnStarted`, `StreamReceived` (carries Crystal's
  `ChatStreamEvent`), `ToolCallsIssued`, `ToolResultsReceived`,
  `ModelRoundClosed`, `RetryScheduled`, `UsageChanged`, `TurnFinished`,
  `QueueChanged`.
- Conversation: `HistoryReplayed`, `ConversationCleared`, `TodosChanged`.
- Text and reports: `NoteWritten`, `ErrorWritten`, `HelpRequested`,
  `StatusReported`, `ToolsListed`, `TodosListed`.

Events carry plain data. `ToolsListed` carries a `ToolCatalogReport` (Plan and
Work definitions, external tool metadata and notes, the external-tools switch,
and the external approval policy), not the settings object or the live
catalog. `HelpRequested` carries plugin verbs as `SlashCompletion` values, not
plugin objects. `SessionEventDataTests` fails when an event can reach
`HarnessSettings`, a provider definition (which may hold a literal API key),
`ExternalCatalog`, or a plugin type.

The engine publishes from whichever thread produced the change, including the
turn thread while a model round streams. Observers must be thread-safe and
return quickly; a front end with a UI thread marshals to it. Events carry
engine values, not captions: the engine reports `SessionActivity` and the front
end chooses the wording and layout.

The engine owns the usage numbers. It keeps the context usage and the session
cumulative totals, including the baseline that in-turn cumulative counts are
added to, and reports them through `UsageChanged` and `TurnFinished`. A front
end renders them and does not compute totals.

Some engine calls take time (a turn collecting, `/compact`, a model switch).
The front end awaits them without freezing its surface. The terminal host does
this by pumping the frame while the call runs.

Operator tool sets and plugins load on the thread pool, not on the caller's
thread, so that pump keeps running. `ExternalCatalog.Load` and
`PluginCatalog.Load` take a per-item callback and a token checked before each
item; an item that started loading finishes. Each item publishes
`LoadingProgressed` (tool set, plugin, or MCP server, with its name) inside the
`ActivityChanged` loading window; MCP servers connect together, so all of them
are named when connection starts. `SessionState` does not keep these names.
`StartAsync` loads with its own token. In a live session only a plain
`/tools reload` or `/plugins reload` is cancellable: `TryInterrupt` stops it
between items, the previous catalog stays, and the note
`Reload cancelled. The previous catalog stays loaded.` is written. Every other
reload (`/tools on|off`, approval and tool set changes, plugin changes,
`/cd`, `/space`, resume into another workspace) runs to the end, because its
settings or workspace root have already changed.

Image markers cross the boundary as text. `UserMessageSent` and replayed
transcripts carry `[Image #N]` markers, and a marker is trusted only when it
starts with `ImageMarkerText.Prefix` (U+2063). A composer that attaches images
prefixes exactly the attached spans. Display cannot reference the engine, so it
holds its own copy of the prefix; `ImageMarkerContractTests` keeps the two
equal.

Adding a front end means referencing CrystalCode.Engine, implementing the five
contracts above, and projecting events onto its own surface. The terminal host
in CrystalCode is the reference implementation. The headless test in
CrystalCode.Engine.Tests is the smallest correct driver. A later Avalonia
desktop front end uses C# markup and does not add XAML or AXAML files.
`crystal bridge` is the Agent Client Protocol (ACP) front end on this
contract ([docs/bridge.md](docs/bridge.md)), not an engine protocol. It uses
`SessionLauncher` with `SessionTrustCheck.Prompt` and a form-backed trust
prompt only for a client with forms, `SessionLaunchRequest.AdditionalRoots`,
`SessionFrontEnd.McpServers`, `SessionRunner`, event origins per prompt,
`ToolCallSummary` and `ToolFileChanges` for tool titles and diffs, the
`SessionOptionsChanged` catalog for config options, the engine's
plain-text report renderers, `CompactionStarted` and `CompactionFinished`,
`SessionLaunchRequest.Fork`, and its own `SessionState`, and maps every event
to ACP or a `_crystal/*` notification itself.

A front end may pass MCP servers for one session through
`SessionFrontEnd.McpServers`. They use `McpServerSource.Session`, are merged
after Home and Project by name (`McpServerDiscovery.WithSession`), follow the
Project approval choice, are not saved in the session file, and their values
appear in no event or text. `SessionLaunchRequest.AdditionalRoots`, when not
null, is the complete set of write roots outside the workspace: the new
session drops saved and default outside rules and keeps read rules on or
inside the workspace (a new session seeds those from `access.json`). Neither
is written to `access.json`. `SessionOptionsChanged` carries the selectable
session settings, each value naming the slash command that selects it; the
session publishes it at start and whenever a value or its choices change, and
not when nothing changed. `CodingSession.TryAttachImageFile` attaches an
image file with `/attach`'s checks and returns the marker instead of writing
a note. `HelpText` and `StatsText` render `/help` and `/stats` as plain text
beside `StatusText` and `ToolListText`.
`crystal server` is the HTTP front end on this contract
([docs/server.md](docs/server.md)). It uses `SessionLauncher`,
`SessionRunner`, `SessionState`, and event origins, and maps every event to
wire JSON itself.

`crystal run` is the headless front end in this executable, not a second
assembly. Refactoring and optimizing it is high-priority product work and
does not change the contract below until a specific change defines otherwise.
It reads one task from an argument or from stdin, applies
process-only overrides, starts one user turn, and exits. The interactive
`crystal` and `crystal space` commands apply the same session overrides:
approval, the approval model and its thinking gear, service tier, and speed,
extended approval and its model, gear, tier, speed, trigger, and
primary-review switch, Plan, Work, or a custom
mode (`--mode`), thinking,
the selected model's context budget, service tier, speed, prompt set, prompt attachments, skills,
external tools, plugins, and turn quotas. Overrides are not written to
`config.json`. That includes the
approval-model switch
(`--approval-model`, `--approval-provider`, `--approval-model-id`), gear
(`--approval-thinking`), and slots (`--approval-tier`, `--approval-speed`),
and every `--extended-approval` flag.
A later interactive preference command writes the field the operator changed,
and can also write a `--provider` or `--model` passed at launch. It does not
write the other launch overrides. The task text, `--format`,
`--show-thinking`, `--space`, and `--workspace-trust` stay on `crystal run`.
Slash commands are rejected on `crystal run` so they cannot change
saved settings. Secrets are not command flags.

The run supplies `RunLog` or `RunJsonLog`, `UnattendedApprovalPrompt`,
`UnattendedUserPrompt`, `UnattendedSessionChooser`, and
`UnattendedTrustPrompt`. The trust prompt is a fail-closed fallback.
The directory check itself runs before the session is created and follows
`--workspace-trust`, not `workspaceTrust`. The approval prompt
denies every call that would have asked the operator. The question prompt
dismisses, and the question tool tells the model it is not supported in the run. Session choice is declined. Review and Audit still call the
reviewing model: allow executes the tool, and deny returns the reviewer's
reason. When the reviewer request fails or returns unusable output, the
model is told an error prevented the approval request from being processed,
not that the user declined, and the text does not suggest asking the user. The error detail stays out of model text. An enabled extended approval model runs the same way after a primary
allow. Plan does not offer write, edit, apply_patch, or bash. The approval policy
also rejects those side effects when a catalog still contains them.

`--format default` writes a readable trace. Each model round prints thinking
text only when `--show-thinking` is set, then the assistant reply, then each
tool beside its own result. With `--show-thinking`, the reply is labeled
`[Assistant]`. `read`, `list`, `glob`, `grep`, and other successful
tools keep a short head excerpt and an omitted-line count. `edit`, `write`, and
`apply_patch` keep their result. `bash` keeps the command, the exit status, and a short
tail, with a longer tail when the command fails. Other failures keep the
result text. Labels use square brackets, exit status uses parentheses, and
omitted-line markers use angle brackets. `--format json` writes one JSON object per
line, with `type`, a UTC `timestamp`, and `sessionID`, and keeps the full
tool output. Event types are
`step_start`, `text`, `reasoning`, `tool_use`, `error`, `note`, `retry`,
`step_finish`, `stopped`, and `session`. `text` and `reasoning` carry one
segment each, not stream deltas. A `retry` discards the failed attempt's
unsent segment. `tool_use` carries `callId`, `status`, and `output`, and
`name` and `arguments` only when a call with the same id was issued; a
result is never paired with another call. `output` shows trusted image
markers as plain `[Image #N]`, and `images` lists those numbers.
`step_finish` carries `reason`, `modelCalls`, `toolCalls`, `contextWindow`,
and three usage objects with `inputTokens`, `outputTokens`, and optional
`reasoningTokens`: `usage` for the turn total, including children,
`lastRequestUsage` for the final request, and `cumulativeUsage` for the
session. An object is omitted when a provider did not report it. A turn
that faults before it reports a result still gets a `step_finish` with
reason `failed` and no counts. Child events reuse the parent shapes with a
`child_` prefix (`child_text`, `child_reasoning`, `child_tool_use`,
`child_error`, `child_note`, `child_retry`) and add `childSessionID` and
`invocationID`. `child_status` reports `agent`, `label`, `status`,
`modelCalls`, `toolCalls`, `images`, and, once known, `usage`.
Thinking text is omitted unless
`--show-thinking` is set. The process then prints the saved session id.
Exit 0 means the turn completed and no operator prompt was denied or
dismissed. Exit 1 is an
invalid command, configuration, credential, workspace, or prompt set. Exit
2 is a failed model request, including a turn that faults before it
reports a result. A cancellation the turn did not request, such as a
request timeout, is a failed request, not an interrupt. Exit 3 is a model-call, tool-call, or duration
budget, or context overflow. Exit 4 is a finish that was not a failure,
budget stop, or interrupt, after an operator denial or a dismissed question.
Exit 5 is an interrupt. A failure, budget stop, or interrupt outranks a
denial.

## Display

CrystalCode.Display is the TUI host. Spectre.Console supplies markup,
color, panels, grids, rules, and padding as an offline rasterizer.
Process startup sets console output to UTF-8 without a BOM, binds
Spectre's profile to the matching UTF-8 writer, and turns its Unicode
output on, then restores the previous output encoding when the process
exits. Changing the code page replaces `Console.Out`, so the profile is
rebound to the new writer; otherwise Spectre would keep encoding with the
old OEM code page. Console input encoding is left alone. Files, prompts,
and provider bodies are already UTF-8. The progress spinner is braille, so
a Windows OEM code page would replace each frame with `?` and garble CJK
text.
`AnsiConsole.Live` is not the session shell: it fights the composer.
Widgets are rasterized into frame rows. A live user, thinking, tool, or
error card keeps rows that are already wrapped. New stream text reflows
only the open tail, and scrolling or typing reuses those rows. The shell enters the alternate
screen when the terminal is a TTY and paints a retained frame: transcript
viewport, optional overlay, optional pinned todos, optional progress row,
status bar, and multiline composer. Unchanged
rows are left in place; a width or height change clears the buffer. The
frame uses one terminal-size snapshot, and the composer window is projected
again if overlays or pinned rows leave fewer rows than it requested. Composer
tabs display as four spaces while the submitted prompt retains tab characters;
other painted rows expand tabs by the same width. One column remains free for
the cursor at the right edge. The executable maps engine events onto that frame
through `SessionProjection` and `SessionRenderer`; it does
not paint rows itself. Entering the alternate screen sets the window
title to Crystal Code when the terminal allows it, and restores the
previous title on exit. If setup fails after the alternate buffer is
entered, the shell leaves that buffer, turns off bracketed paste and
mouse reporting when those were enabled, and restores the title when
it was changed. Windows VT input mode is restored on screen disposal,
with a process-exit retry if the first restoration fails. When the terminal
drops below the usable minimum
(`ShellLayout.MinUsableWidth` x `ShellLayout.MinUsableHeight`, 80x24),
the frame is replaced by a centered resize notice sized to the real
terminal and input is ignored, including keys typed while the window is
too small. The session resumes automatically once the window is large
enough. `/stats` also replaces the frame: the status bar, composer,
queue, todos, and progress row are not painted, and keys other than
Esc, `q`, and scrolling are ignored. The page is a centered column of
Overview, Tokens, and Tools panels. Tool rows show a share bar, count,
and percent. Esc, `q`, or Ctrl+C restores the session frame and leaves
the composer draft in place. `ShellLayout.MinWidth`/`MinHeight` (16x8) remain a math floor
for layout only.

Text that came from a model, a tool, or a file never reaches the terminal as
control bytes. `TerminalText.Sanitize` removes C0 and C1 controls, DEL, and
escape introducers from streamed and committed text. Tool bodies still collapse
a carriage return the way a terminal overwrites the current line. One-line
chrome (`SanitizeLine` for todos, activity, and progress) flattens carriage
returns, line breaks, and tabs to spaces and does not use that overwrite.
`PaintLine.Fit` strips controls again from every row after tab expansion, so
no producer can leak an escape sequence into a frame. Todo content, activity
and progress text, and widget segments go through the same path. Approval
cards are different: the
operator must see what is being approved, so control and bidirectional
characters in commands, diffs, rationale, and tool summaries are shown as
visible `\xNN` and `\uNNNN` escapes through `TerminalText.Reveal` instead of
being dropped.

Width policy: `TextWidth.Measure` must never be narrower than Spectre's
`Segment.CellCount()`. `WideRanges` is generated from Spectre's own widths,
and a test compares every code point. Each row is rasterized by `FrameRow`
at a very wide virtual width and cropped at a cell boundary, so Spectre can
never re-wrap a row and emit a line feed into the frame. Known limits:
variation-selector emoji sequences are measured as one cell, and ambiguous
width characters assume a narrow terminal.

`ScreenPainter` wraps every paint in DEC 2026 synchronized output
(`?2026h` and `?2026l`), which terminals that lack the mode ignore.
Autowrap is disabled while rows are written. Before it is enabled again
the cursor is parked at the origin, so a full-width row cannot leave the
pending-wrap flag set and shift ASCII onto the composer. Unchanged rows,
including the composer, stay in place. On Windows the console echo flag
is off so the host does not print mouse and mode reports into the input
line. A finished report whose escape was already consumed is dropped, not
pasted. An unfinished tail is still text, so a paste such as `[12;40` is
kept.
`WidgetPaint` keeps foreground, background, bold, dim, italic, underline,
strikethrough, and invert when it rasterizes Spectre widgets. Links and
blinking are dropped on purpose.

`ScrollAnchor` keeps the transcript on the row the operator is reading. That
position is one committed row, named by entry index and line. A live tail
uses the entry index it will occupy once committed. New rows at the end move
the distance from the bottom so the anchored row stays put. At the bottom,
where no row is anchored, the view follows new output. A scroll that leaves
the bottom is measured from the latest row count. A width change keeps the
distance and re-clamps it, then takes a new anchor. When a block above the
anchor changes height, the same row is found again.

Terminal.Gui is referenced from CrystalCode.Display with a floating
version and is not used. Do not call `Application.Init` or mix a second
console writer with the self-owned loop.

The status bar shows the selected context budget as `Context 272K` beside
the thinking gear. The custom status-line field `context-window` shows the
same value and is included in its default field list.

The status bar shows approval, thinking (when the selected model
supports it: `Think Off`, or `Think` plus the resolved gear when
thinking is on), `Tier <token>` and `Speed <token>` when those slots
are set, the non-default prompt set as `Prompt <name>`, model,
workspace, the latest request's context percent (`CTX`), cumulative
token counts (`IN` / `OUT`), and, when the bar has room, a cumulative
Title Case total (`783k Total`). During a turn the status bar updates `CTX`, `IN`, and `OUT` after each
model round and each tool batch: provider usage as it arrives, then a local
transcript estimate marked `~` after tools until the next round reports. It also
shows tool count (`Tool` / `Tools`), and
elapsed time. When the session has todos, a pinned `Todos` bar sits
above the progress row and status bar. The existing adaptive status bar is the
default. Operators may independently enable an ordered custom status line
through `customStatusLine`, `statusLine`, or `/statusline`. Custom token fields
name their scope explicitly as context, request, or session usage. Disabling
the custom line returns to the existing adaptive rendering path.
A disk plugin may register extra field names. Those names stay off the
adaptive bar, the progress row, `/status`, and `/stats`. They appear on the
custom line only when the operator lists them. The engine publishes the
latest plain text as `StatusFieldsChanged`, in the order the changes were
taken. The terminal stores that text and paints it on the next input poll.
The host does not poll the plugin and does not run a status timer. Empty
text occupies no cell. A narrow
custom line still drops `custom` slots from the right.
Each row is a checkbox colored
by status: pending chrome, in progress accent, completed ok, cancelled
muted. The bar shows the first `todoBarRows` items (3 to 10, default 4) and a `+N more` line when
the list is longer; `/todos` (alias `/todo`) publishes `TodosListed` and the
terminal draws the full untruncated list in a rounded `Todos` card.
`/todos bar [on|off]` and `/todos rows [3-10]` write `todoBar` and `todoBarRows`
to `config.json` (defaults on and 4; each omitted when default) and publish
`PreferencesChanged`. A `todoBarRows` outside 3 to 10 refuses startup.
Themes: `config.json` `theme` names a built-in (`default` = `dark`, `dark`, `light`; palettes live in `Theme`) or `~/.crystal/themes/<name>.json`
(Home only; a workspace file is never read). `ThemeCatalog` in the engine lists
and reads names and files and touches no console. `SessionPreferences.Theme`
carries the name; the terminal `SessionProjection` loads the file and calls
`Theme.TryApply` in `CrystalCode.Display`, which validates every token as a
Spectre style and changes nothing on failure. `config.json` `approvalColors` (default on, omitted when on) rides on `SessionPreferences.ApprovalColors`; when off, `ShellChrome` paints the approval mode with `chrome` instead of `Theme.ForApproval`. `/theme` and `--theme` (process only,
not saved) select it. Server and Bridge only forward the name. The transcript Result card uses the same colors for
`- [ ]` / `- [~]` / `- [x]` / `- [-]` lines so they are not painted as
diff removals. Named chrome labels are Title Case. Short status
abbreviations are uppercase. Mode (Plan, Work, or a custom mode name) is on the
composer prompt and is not repeated on the status bar. While a turn
runs, a progress row sits directly above the status bar
(`Waiting For Model`, `Thinking`, `Writing`, `Running Command`,
`Awaiting Approval`, `Reviewing`, `Extended Review`, `Waiting For Answer`,
`Compacting`, `Retrying In Ns (Attempt K)`). The retry caption counts
remaining wait down. The same row shows
`Loading Tools` while operator tool
sets are discovered at session start and on `/cd` or `/space`, after the frame is
up, so a slow assembly load does not leave a blank terminal. The
caption names the item loading now (`Loading Tools  docs`). Session start
does not use that row: `TerminalHost` opens the frame and runs
`StartAsync` behind a full-screen startup page (`StartupPage` in
`CrystalCode.Display`, painted by `SessionRenderer.PumpStartupAsync`). The page
lists each `LoadingProgressed` item, discards keys, and maps Ctrl+C to
cancelling `StartAsync`, after which the session is still closed and saved. A
trust or question prompt during startup pauses the composer and draws the
session frame until it is answered. While the activity is `LoadingTools` or
`LoadingPlugins`, `SessionRenderer.HoldSubmission` keeps the composer
editable but turns Enter, F6 send, and F7 into no-ops, so the pump never
queues input that arrives during a reload. The
caption is prefixed with a one-cell spinner
that advances while the turn is live, plus the current activity
elapsed time (`5s`, `2m18s`). When `estimatedTokens` is on, Thinking
and Writing also show a live estimate (`~1.2k Tokens`) after elapsed
time. Elapsed resets when the progress
caption changes. It is independent of the
status-bar activity bullet (`• Bash`). The row is omitted when
idle. Assistant text is
rendered as markdown while it streams and after it commits (headings,
lists, fenced code with a dim code background, inline code and bold, and
GFM tables). A table is aligned by display columns, wrapped inside a
column, or stacked when the terminal cannot give every column its minimum.
A table that cannot be parsed or laid out is painted as its original lines.
User, thinking, tool, and result blocks are rounded panels. A live turn
writes a Tool card when the model round closes with tool calls, before
those calls execute, using the same one-line summary as session replay.
Tool names in chrome and cards are Title Case; stream name chunks are
coalesced per tool call so a repeated snapshot does not become
`ReadReadRead` and sequential calls never concatenate. Success
result panels honor `verboseResults` (`config.json`, default on, omitted
when on). It is the parent of `verboseTools` and `verboseCommands`.
While it is off, every success result is omitted, including read, search,
skill, `edit`, `write`, `apply_patch`, `bash`, `bash_start`, `bash_status`,
`bash_logs`, and `bash_stop`. The Tool card stays unless `verboseCalls` is off.
That switch (default on, omitted when on) hides every Tool card,
including read, search, edit, write, bash, background bash, and
external tools. It does not hide success or failure results, and
`verboseResults` does not hide Tool cards. Failure results stay. The child
switches stay stored and do not draw a result until the parent is on
again; Ctrl+G does not expand a command result in that state because the
panel is omitted, not collapsed. A missing `verboseResults` field keeps
the parent on. While the parent is on, tool verbose off omits
read/search/skill result panels and still shows `edit`, `write`, and `apply_patch`
results, and command verbose off collapses `bash` and the four background
bash tools to a hidden-line hint plus the last output line. `crystal run`
does not read `verboseResults`. Policy and grant auto-pass cards honor `verboseApprovals`
(default on). Review and extended review allow cards honor `verboseReview` (default on). The
switches are independent: turning approvals off does not hide a review allow
card, and turning review off does not hide a policy or grant card. Cards
already in the transcript follow their own switch, and turning that switch
back on shows them again. A saved `verboseApprovals` value of false no longer
hides review allow cards. The ask overlay still appears. Ctrl+O toggles tool verbose and
Ctrl+G toggles command verbose when the composer is empty; `/verbose`
shows or changes the same settings (`tools`, `commands`, `results`,
`calls`, `approvals`, `review`, `thinking`, `on`, `off`). `results` is
the parent of `tools` and `commands`. `calls` hides Tool cards only.
Thinking panels honor `verboseThinking`
(default on). When it is off, live and committed Thinking cards are
omitted, including cards restored with a session; turning it back on
shows the stored text again. Inline output, used when the alternate
screen is unavailable, applies the same switches to restored thinking,
tool results (including the `verboseResults` parent), policy and grant
approval text, and review allow text.
The progress row, the thinking gear, and export stay.
`crystal run --show-thinking` remains a separate one-shot
flag and does not read this setting.
Approval is a Spectre panel with a two-column Title Case field grid
(`Status`, `Reason`, `Risk`, `Authority`, `Outcome`). Question panels show
header tabs, described choices, selection state, custom input, and a final
answer review when confirmation is required. Plain Up/Down stays with question
selection. When the question panel is taller than its slot, PageUp/PageDown,
the wheel, and Ctrl+Up/Down scroll that panel. Moving the highlighted choice,
or the cursor in a custom answer, scrolls the panel to keep that row visible.
A manual scroll stays until that row changes. A scroll that would move past
either end, and any scroll while the panel already fits, scrolls the
transcript. Custom answers edit inside the overlay with an independent buffer:
Enter saves and Escape cancels editing without dismissing the question. The
paused main composer keeps its draft and hides its cursor. Ask and auto-pass
cards for `edit`, `write`, and `apply_patch` show a capped `+` / `-` preview
of `old_string` / `new_string`, `contents`, or the patch. A patch card heads
each file (`Add`, `Update`, `Move`, `Delete`) and lists only its removed and
added lines; the line cap covers the whole card. An `edit` with `replace_all`
adds a muted `Replaces every occurrence` line above the preview. Overlay keys share the
session frame loop, so transcript scroll and resize still work while a
prompt is up. Ask overlays use the same card; `Y` / `S` / `A` / `N` map
Once / Session / Always / Deny for ordinary workspace calls. Calls that cannot
reuse grants offer only `Y` / `N` (Once / Deny) and ignore Session/Always keys.
The follow-up queue is a `Queued` panel
above the composer.

Composer keys: Enter submits when idle and queues while a turn is
running. Queued text stays above the composer and is sent when the
current tool batch or turn ends. Empty Enter while a follow-up is
queued interrupts the turn, or compaction if that is still running, so
the queue is sent now. Empty Enter with nothing queued does not stop
the turn. Backspace deletes one character on every
platform. Windows Ctrl+Backspace deletes a word. On Unix, ReadKey tags
plain Backspace as Control; that is still one character. Ctrl+W or
Alt/Option+Backspace deletes a word. Ctrl+C at idle clears the composer.
Two Ctrl+C presses on an empty composer exit. Ctrl+J or `\`+Enter inserts a
newline. Tab cycles Plan, Work, and enabled custom modes, or completes a `/` command. Shift+Tab also cycles modes. Chrome labels are Plan, Work, Review, Default, Edit, Audit, and Full.
When the slash picker is open, plain Up/Down moves its selection without
resetting it and scrolls the visible candidate window to keep that selection
on screen. Tab accepts the selected completion. Enter accepts a partial
selection first; when the composer already contains that complete command or
argument, Enter submits normally.
Status abbreviations are CTX, IN, and OUT. The status
bar includes a queued count while follow-ups wait. Auto-pass prints a
panel with Status, Reason, Risk, Authority, and, for review, Outcome
plus rationale. Policy and grant panels follow `verboseApprovals`. Review
allow panels follow `verboseReview`. Reasoning streams into the
transcript while `verboseThinking` is on. Built-in slash verbs live in `SlashCatalog` and include
aliases (`/new` is `/clear`, `/continue` is `/resume`,
`/q` and `/exit` are `/quit`, `/think` is `/thinking`, `/summarize` is
`/compact`, `/todo` is `/todos`, `/prompts` is `/promptset`,
`/verbose` toggles tool, command, result, call, approval, review, or thinking detail). `/export`
writes markdown or json for the current conversation; `/prompts export`
writes built-in prompt templates. Export roots default to
`~/.crystal/exports/` (`exportDirectory` omitted or `home`) and are
configurable through `exportDirectory` in
`config.json`. A slash picker appears while the prompt
is a command prefix. After a verb that takes an argument
(`/thinking`, `/approval`, `/model`, `/tokens`, `/verbose`), Tab also completes the argument.
`/model` completes current-provider models, then a provider name, then
that provider's models. `/approval model` completes `on`, `off`, and the
stored approval provider's catalog, and `/approval thinking` completes the
selected approval model's gear. `/export` completes the format, optional system flag,
and the system flag after an explicit path. `/prompts export` offers directory
examples while Enter on the optional argument boundary still submits with the
default directory. Ctrl+O and Ctrl+G toggle verbose tool and command output when the composer is
empty. Plain Up/Down move through the composer's visual rows, including
wrapped rows. At the first or last row they navigate submitted prompt
history; Up on an empty prompt recalls the latest entry, and Down past
the newest entry restores the unsent draft and its cursor. The slash
picker owns plain Up/Down while open, but it stays closed while the
composer is showing a recalled entry, including one that starts with `/`.
Editing that entry opens the picker again. Submitted text-only history is
stored by workspace in `~/.crystal/prompt-history.jsonl` with an
owner-only file mode where supported. Image-bearing prompts remain in
memory only and are forgotten when the session changes, since their
markers do not carry image bytes or a stable cross-session attachment.
PageUp/PageDown are the primary transcript scroll controls;
Ctrl+Up/Down also scroll when the terminal passes those keys through.
The alternate screen enables bracketed paste (2004) and mouse reporting
(1000 with SGR encoding 1006) and turns alternate scroll (1007) off.
While that screen is up, the shell repeats
those modes about once a second. On Windows it also puts VT input back and
turns quick edit and native mouse input off, then restores the console
input mode captured at entry when the screen closes. The next refresh puts
the wheel back if the console host cleared it. A wheel report scrolls the transcript and is
drained without waiting; other mouse reports are swallowed. Unix ReadKey
reports an X10 click (`ESC [ M` plus three bytes) as F1 followed by those
bytes; that burst is still a mouse report and is not composer text. A lone
F1 is held briefly on Unix so a split payload stays with the prefix.
Windows VT delivers the same report as characters, so F1 is not delayed
and is not treated as a mouse prefix. The wheel is
never converted to Up/Down, so plain Up/Down stay with the composer, prompt
history, or the active selection. Terminal text selection needs Shift
(Option or Fn on some macOS terminals) while mouse reporting is on, and the
help list says so. Escape clears the composer. Pasted text is normalized to
LF and tabs, and control, invisible, and bidirectional formatting characters
are dropped. Escape is held only
when no further bytes are available or the sequence is still incomplete.

`KeyBurst` collects one `ReadKey` drain. `InputDecoder` turns that burst
into `InputKey`, `InputPaste`, or `InputWheel`. Platform differences stay
in the decoder; the composer, scroll policy, and overlays consume events
only. Linux and macOS usually deliver parsed `ConsoleKey` values. Windows
VT input leaves `Key` empty, so Tab, Enter, letters, and CSI arrive as
`KeyChar`. The shell restores the prior Windows console input mode on exit.
macOS Option-as-Meta is `ESC` plus a letter or Backspace and
becomes Alt; a native Alt modifier on a parsed key is kept. A CR+LF drain
is one Enter, not paste. Paste is the text between CSI `200~` and `201~`.
A printable burst with an internal newline is still paste when those
markers are absent, so it is inserted and does not submit. A burst whose
only newline is one trailing CR or CRLF stays keys, and so does a burst
that contains Backspace or Delete. Escape sequences that are not a
bracketed-paste wrap are not treated as paste. The host does not parse VT. The frame polls terminal
size and repaints when the window is resized. Redirected output stays
sequential.

## Plugins

First-party `IPlugin` contributes tools, chat-client factories, approval
classifiers, or slash commands through `PluginContribution`. A tool
contribution always has a text `ITool` implementation and may additionally
provide an `IMultimodalTool` with the same definition name; the latter may
return generic image content while reusing the host approval policy.
Every contribution factory must return a definition whose name matches the
registered contribution name; the host rejects a mismatched definition before
it enters a catalog.
Built-in tools and all six wire protocol adapters register through that
table. `PluginRegistry` does not load assemblies from disk. Tool contributions
default to `Append`, which rejects duplicate names. An in-process contribution
may explicitly use `Replace` with a reserved built-in name; the target must
already be registered by the built-in workspace plugin. Replacement is
selected per Plan or Work catalog, keeps the built-in model-facing name, and
must return a definition with that same name. A replacement is used for both
text and multimodal creation when it supplies a multimodal tool; it does not
inherit the original multimodal implementation. The resulting tools still
pass through the same approval policy, executor, cancellation, and budget
boundaries. Disk plugins and external tool sets stay append-only.
Approval classification remains keyed by the model-facing tool name. A
replacement for `read`, `write`, `edit`, or `bash` therefore inherits that
name's built-in classification; the host does not infer a replacement's actual
side effects from its implementation. In-process replacement is consequently
a trusted extension point and must not be exposed to untrusted disk or
external sources without a separate classification contract.

Disk plugins use `CrystalCode.Plugins.IPlugin` and are loaded by
`PluginCatalog` from `plugins/`. One non-collectible load context serves
each plugin. Shared contracts (`Crystal`, `Crystal.Tools`,
`CrystalCode.Tools`, and `CrystalCode.Plugins`) come from the host.
`CrystalCode`, `CrystalCode.Engine`, `CrystalCode.Display`, and
`CrystalCode.Providers` are refused. A disk plugin may contribute tools,
and a tool that implements `IHostTool` or `IHostMultimodalTool` receives a
`ToolHostContext` captured when that call starts. It may also contribute
a protocol factory for a protocol the built-in adapters do not own,
classifiers for unknown tools, slash commands that do not reuse a
built-in verb, hooks, raw hooks, and prompt placeholders. After plugins,
external tools, and skills have loaded, the host calls `IPlugin.Attach`
with a read-only snapshot and calls it again when those catalogs reload.
The snapshot lists discovered plugin directories and external tool sets
(directory, source, enabled, effective, loaded, and skip reason), loaded
external tools (name, set, source, and Plan or Work), and the skills the
`skill` tool can load (name and description). It includes the calling
plugin. It does not include assembly paths, manifest paths, built-in
tools, or skill file bodies. `Contribute` runs before that snapshot
exists. The snapshot does not enable, disable, invoke, or reorder other
plugins or tools. The host then calls `IPlugin.AttachSession` with a live
view of the session model and the review model. Each read returns the
current provider, protocol, model name, context window, maximum tokens,
temperature, top-p, image input, declared audio input, declared video
input, thinking gear, and the selected service tier and speed when set.
The plugin model's `ContextWindow` describes the
configured maximum model capability, not the selected local session budget.
`/context` does not change that capability value; chrome and usage events
carry the selected budget used by compaction and `/status`.
Audio and video flags copy the model entry.
They do not make a turn, a tool result, or a plugin multimodal client
accept audio or video. `Review.Independent`
is true only when review uses its own model. While it is false, review
uses the session model and a stored selection is omitted. The view has no
API key, endpoint, organization, or project. A plugin that implements
`IPluginModelClient` also receives `AttachClients`. The host names that
plugin when it loads. Those clients are new instances on the same provider
and model, kept apart from the instances inside a turn or a review. A plugin
that implements `IPluginDataDirectory` receives its global and project
runtime-data directories through `AttachDataDirectories`.
A plugin that admitted at least one status field then receives
`AttachStatus`. The sink accepts only that plugin's names. A later catalog
reload retires the previous sink. Tool reloads do not attach again. A failed
attach drops any text that sink already published. `PluginRegistry`, external tool sets, and
`crystal run` do not receive one.
`IndependentReview` matches the review flag. `Review` is absent while that
flag is false. When the flag is true and the review client cannot be
created, the read fails and the session client stays in place. The host
drops a side-channel client when it rebuilds the session client or the
review client, and when review stops using its own model. `Contribute` runs before either call.
`PluginRegistry` does not use them. Placeholder resolution receives the
same model view. Hooks append prompt and compaction text,
rewrite a user message before it is stored, and revise the text of one
outbound model request without writing the archive, so the model can see
text the archive does not store. Hooks also read
one returned model response
without changing the candidate the host uses, rewrite a tool call before
approval runs again, replace a tool result, and raise approval risk or
require another prompt. They do not lower risk, skip approval, or replace
Work, Plan, or Review. Session start and end follow the coding session.
`/clear`, `/resume`, and `/fork` end the session being left and start the
one that replaces it. A workspace change ends in the directory being left,
reloads plugins, and starts in the new directory. A resume that also
changes workspace is a single end and start.

Authority is decided per method, and a method with extra authority lives
on its own interface with its own registration list. `IPluginRawHook` is
that interface. A plugin registers it on `PluginContribution.RawHooks`,
apart from `Hooks`, so the host can tell which plugins hold privileged
methods without inspecting types. The host announces each such plugin
with a note and asks the operator for nothing. `RebuildModelAsync` runs
before ordinary model hooks, in plugin load order. It rebuilds one outbound
request in any way the host can represent: drop, reorder, add, or rewrite
items of any kind, including the live system prompt. The host refuses only
what it cannot represent: a repeated item id, empty reasoning text, or an
image the session does not hold, which includes any added image on a request
that cannot carry images (`PluginModelRequest.AcceptsImages`). It does not
check provider validity. When a work or plan call fails after a raw hook
changed its request, the engine writes a hedged note naming the hook. That
rebuild does not write the archive.

`RewritePrompt` and `RewriteCompaction` run after the ordinary append for
that text. Each may replace the full string with any text, including an
empty string. `RewritePrompt` replaces the composed system text for work,
plan, review, and compaction. Work and plan text becomes the live system
message and is archived; the files under `~/.crystal/prompts` are not
written. `RewriteCompaction` replaces the compaction user prompt, or the
summary body that is stored. It does not choose which history is folded.
`RewriteApproval` runs after ordinary approval hooks and may set any risk,
any authority, any summary, and either prompt requirement. The approval
policy then uses that classification. Ordinary hooks still cannot lower
risk or replace those prompts. A raw hook is not held to those rules. A
throwing raw hook is skipped. New raw methods join this interface and never
ordinary hooks. More hook methods will be added later on these interfaces.
They are not reserved as empty types in advance. Whether plugins declare
dependencies on one another, and related loading questions, are under
consideration and are not decided.

A later module system may sit below plugins and raw hooks and reach the
host through reflection or another mechanism. It is not in this build. Its
behavior and ownership are not defined, and this build does not reserve
types for it. Catalog order is
built-in tools, disk plugin tools, external tools, then `skill`.

`/plugins enable|disable|show` and `/tools enable|disable|show` change one
directory. `home` or `project` selects the tree; otherwise a project
directory wins. `crystal plugins` and `crystal tools` perform the same
manifest edit outside a session. Their default output is a Spectre.Console
table. `--format text` prints aligned plain text. They write `enabled` on
that manifest and do not write `config.json` or start a session. The
global discovery switches stay separate.

Further plugin capabilities are deferred. They include extending the process
command line and custom tool execution modes. The set is not closed. This
build does not reserve a command, flag, configuration field, or public type
for them. An in-session slash command is not a process command. A custom
execution mode is not a host concurrency setting. A plugin still cannot
bypass approval, trust, the workspace fence, or `ToolInvocationPolicy`.

Operator tool sets are not plugins. They are discovered from `tools/` and
wrapped by `ExternalCatalog`. An exec child starts in the workspace root
and receives `CRYSTAL_WORKSPACE`, `CRYSTAL_SESSION`, `CRYSTAL_APPROVAL`,
`CRYSTAL_TOOL_DATA`, and `CRYSTAL_PROJECT_DATA`
on that process only. `"output": "content"` reads one JSON object from
stdout (`text` and optional fenced images). A dotnet set uses one non-collectible
`AssemblyLoadContext` for that directory only. Shared contract types
(`Crystal`, `Crystal.Tools`, `CrystalCode.Tools`, and already-loaded
`System.*` / `Microsoft.*`) come from the host context that already
loaded `Crystal.Tools`. Public `ITool` and `IMultimodalTool`
implementations are loaded directly. A type may implement either contract
or both; when it implements both, both definitions must match. A type may
also implement `IHostTool` or `IHostMultimodalTool`. The wrapper then
passes a `ToolHostContext` captured at the start of that call (workspace
root, session id, approval mode, and the set's global and project data
directories). Other tools keep the original
`InvokeAsync`. Native multimodal tools join
the active catalog only for an image-capable model and provider. The external-tool loader does not implement `CrystalCode.Plugins.IPlugin`
and does not scan `plugins/`.

Environment variables:

- `CRYSTAL_HOME` overrides the data directory.
- `DEEPSEEK_API_KEY`, `OPENAI_API_KEY`, `GEMINI_API_KEY`, and `CRYSTAL_API_KEY` override
  `credentials.json` for that process. A provider-specific variable wins over
  `CRYSTAL_API_KEY`. The persistent store is `credentials.json`.
