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
If that desktop front end is built with Avalonia, write the UI in C# markup.
Do not add XAML or AXAML files.
`EngineAssemblyTests` and `DisplayAssemblyTests` fail the build when either
direction above is crossed.

CrystalCode.Tests references CrystalCode.
CrystalCode.Engine.Tests references CrystalCode.Engine.
CrystalCode.Display.Tests references CrystalCode.Display.
CrystalCode.Providers.Tests references CrystalCode.Providers.

## Assembly ownership

### CrystalCode

The terminal host and the only executable. Owns CLI commands, the terminal
host loop (alternate screen, key loop, Ctrl+C), the headless `crystal run`
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
facts. It defines `ToolHostContext`, `IHostTool`, and
`IHostMultimodalTool`. It references only Crystal and Crystal.Tools. It
does not reference the engine, the display, the executable, or a terminal
library. Tools that implement only Crystal's `ITool` or `IMultimodalTool`
do not reference it. There is no paired test project; dispatch and
load-context identity are tested in CrystalCode.Engine.Tests.

### CrystalCode.Plugins

The contract a disk plugin references. It defines `IPlugin`, contribution
lists, the hook and raw hook interfaces, and the hook context types. It references only Crystal and
Crystal.Tools. It does not reference the engine, the display, the
executable, or a terminal library. There is no paired test project;
discovery, the load context, and hook order are tested in
CrystalCode.Engine.Tests.

### CrystalCode.Engine

The front-end-neutral class library. Owns session and turn execution,
Plan/Work catalogs, approval policy, context compaction, `~/.crystal`
storage, built-in coding tools, operator tool sets, operator plugins,
prompts, in-process plugin contracts, slash commands, and the event stream a front end observes.
It never reads a key, writes to the console, or paints. See
[Engine contract](#engine-contract).

### CrystalCode.Engine.Tests

Engine tests: workspace fencing, approval classification, compaction
selection, session serialization, command behavior, external tool sets, a
headless session driven by a scripted model, and the dependency guard. Does not
reference the host executable.

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
| `Commands` | Spectre.Console.Cli commands. Bare `crystal` stays the terminal. `space` opens the operator space under the data directory. `run` is the headless task command. `version` prints the build identity |
| `Run` | Unattended front end for `crystal run`: plain-text or JSON-lines log, denied operator prompts, dismissed questions, process-only setting overrides, and exit codes |
| `Terminal` | Host loop, event projection, session renderer, status and tool-list widgets, progress and tool-call text, transcript replay, session picker, question overlay, slash-option mapping |
| `Terminal/Approvals` | Approval prompt, card, diff preview, and keys |

CrystalCode.Engine (namespaces below are relative to `CrystalCode.Engine`):

| Folder | Owns |
| :--- | :--- |
| `Configuration` | Loaded options, defaults, thinking and mode labels, display-case helpers |
| `Events` | Immutable session events, observer interface, chrome and preference snapshots |
| `Home` | `~/.crystal` paths and file I/O |
| `Sessions` | Session (`CodingSession`), transcript, ledger, streaming turn, slash commands and completions, chat client lifetime, status, usage, and tool-list text, front-end contract (`SessionFrontEnd`, `ISessionChooser`) |
| `Approvals` | Risk, authority, grants, policy, review transcript |
| `Approvals/Interfaces` | Prompt and reviewer contracts |
| `Compaction` | Window accounting and summary substitution |
| `Tools` | Workspace fence and built-in `ITool` types |
| `Tools/External` | Operator tool sets (`tools.json`), exec and isolated `ITool` / `IMultimodalTool` loaders, and host-context dispatch |
| `Prompts` | Caller-owned system text. Built-in Work and Plan identify the assistant as Crystal Code |
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

Provider types live under `CrystalCode.Providers` plus one folder per wire
family (`DeepSeek`, `OpenAI`, `Responses`, `Anthropic`, `Gemini`, and `Ollama`). Shared
OpenAI-compatible Chat Completions request and stream parsing lives in
`Compatible`. Shared direct-HTTP lifecycle and SSE framing for Responses and
Messages, Gemini GenerateContent, and Ollama Chat lives in `Protocol`; each
wire codec still owns its request and event semantics. Gemini uses SSE and
replays signed response parts so function calls retain thought signatures.
Ollama uses JSON lines and native `tool_name` results. Outbound chat requests send a constant `User-Agent` of `Crystal Code`
with no version. Provider SDKs are not used.

## Crystal consumption

The interactive turn uses `IStreamingChatClient` and `ToolExecutor`. When the
selected model declares `imageInput` and its adapter supports it, the session
uses `IStreamingMultimodalChatClient` and a host executor that preserves the
same approval policy. The session owns those clients and may replace them on
`/model`. It does
not use `Crystal.Agents.Agent` for the live UI: that Agent completes model
turns without token streaming.

The live session does not yet use `Crystal.Harness.AgentHarness`. Parent/child
Agent composition is deferred product work; when that topology is implemented,
the host consumes `AgentHarness` for named invocation ancestry and shared
budgets while retaining ownership of prompts, tools, approval, persistence, and
the terminal projection.

Approval is a `ToolInvocationPolicy` supplied to `ToolExecutor`. Rejection
returns Harness-authored `ToolOutput`. Tool exceptions become model-visible
only through a Harness `ToolExceptionMapper`.

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
model or provider does not support image input.

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
sending a request. Audio/video input and non-text model output are TODO. MCP
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
   exponential backoff (2s base, factor 2, 25% jitter, 30s cap when
   Retry-After is absent), honor Retry-After or retry-after-ms when
   present, and repeat the same round up to five times. The engine
   publishes `RetryScheduled`; the terminal progress row shows
   `Retrying In Ns (Attempt K)` and counts the remaining wait down, and
   the transcript prints `retrying model request` plus the operator
   message. Live stream text from the failed attempt is
   discarded. HTTP 401, 403, quota, `invalid_prompt`, and context
   overflow are not retried. User cancel still interrupts immediately.
3. Select candidate zero.
4. Stop before tools when the finish reason is not a normal stop or a tool
   request. `length` keeps the assistant text, drops tool calls, and stops
   as `output_truncated`. A content filter keeps the text and stops as
   `content_filtered`. Any other reason stops as `failed` and publishes
   `Model stopped with finish reason '<value>'.` These stops do not compact.
5. If the candidate has tool calls, execute the full batch through
   `ToolExecutor`, or the multimodal bridge for an image-capable turn
   (approval runs first in either case). A batch that does not fit the
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
`config.json`. Omitting it keeps 120 seconds. `null` or `"unlimited"` disables
that timer. A positive integer up to the runtime timer limit replaces it.
User cancellation and the turn budget still stop the command. External tool
sets keep their own `timeoutSeconds`.
7. After a completed turn, consider compaction from that last round's
   reported usage and the estimated model context. `/compact` (alias
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
Images stay when either transcript still mentions them. `/clear` drops both.

The composer stays open while a turn runs. Enter with text enqueues a
follow-up (FIFO). Queued items stay in a panel above the composer until
they are sent. The queue is sent when the current tool batch finishes or
when the turn (thinking or conversation) ends. Empty Enter while a
follow-up is queued interrupts the turn, or compaction if that is still
running, so the queue is sent now. Empty Enter with nothing queued does
not stop the turn. Ctrl+C interrupts and does not drop queued text.
At an idle prompt, Ctrl+C clears the composer.
Two Ctrl+C presses on an empty composer exit.

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

These are product modes, not Crystal types.

- Plan registers read, list, glob, grep, todowrite, todoread, and question. When
  Skills is enabled, it also registers skill. External tools whose
  `catalogs` include `plan` are appended after the built-ins.
- Work registers those tools plus edit, write, and bash, then external
  tools whose `catalogs` include `work`.

The built-in `question` tool accepts an ordered question array. Each question
has a short header, full text, described options, optional multiple selection,
and custom input enabled by default. A single single-select question submits
immediately. Multiple questions and multiple-select questions use a shared
confirmation step. Answers return in question order as arrays of selected
labels; dismissing the overlay returns a failed tool result.

Built-in Plan still has no edit, write, or bash. An external Plan tool
keeps a Write + Workspace floor, so Plan is not built-in reads only
once such a tool is installed. Approval still runs.

Switching modes replaces the first system message and the executor catalog.
The transcript is otherwise the same conversation.

The todo list belongs to the session, not to a mode. Built-in Plan text asks
the model to write the plan's steps as pending todos and leave them pending;
built-in Work text asks the model to work through pending todos it finds
instead of starting a new list. Built-in Work and Plan text also tell the
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
same placeholder syntax. Composite session slots are `{{env}}`, `{{skills}}`,
and `{{instructions_section}}` or raw `{{instructions}}`. Atomic session slots
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
is `plan` or `work` for Work and Plan, `review` for Review system text, and
`compaction` for compaction system text. The `{{env}}` block includes the
operating system, architecture, and local time, and adds git root, session,
and approval lines when the host has them. Review user templates add `{{conversation}}`,
`{{tool_name}}`, `{{tool_arguments}}`, `{{host_risk}}`, `{{host_authority}}`,
and `{{classification_summary}}`. Compaction user templates add
`{{conversation}}`, `{{prior_summary_section}}`, `{{summary_task}}`,
`{{output_template}}`, and `{{todos_section}}`. A disk plugin may add further
names through `IPluginPlaceholder`. The host rejects a name it already owns,
and the first loaded plugin keeps a duplicated name. Those values expand in
the same binding pass as the host names, including attachments, Review, and
compaction. A returned value is inserted as text, so tokens inside it stay
literal. A resolver that throws, or returns no text, leaves the token and
records an English note. Built-in Work, Plan, Review,
and compaction templates declare the slots they need explicitly. Topic naming
has a built-in prompt and is overridden by `topic.md` in Home or project
`prompts/`; it is not a prompt-set member and is not yet invoked by the live
session. Review system text is the named file plus any enabled review
attachments. It does not receive the workspace instruction block. Its user
turn is template-driven. Composite
and atomic host values are not overlay files and refresh on `/cd`, `/model`,
`/approval`, and when the live system message is replaced. Skill guidance is host-owned
and is not an overlay file.

Prompt set selection and direct prompt overrides are separate features. A
prompt set is a Home-only directory under `~/.crystal/promptsets/<name>` with
`prompt.json` and any subset of `work.md`, `plan.md`, and `review.md` (`.txt`
is also accepted). The host never scans a workspace for prompt sets.
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
override. Instructions, skill guidance, the environment block, and compaction
text are not prompt-set members. `/promptset` (alias `/prompts`) lists sets and
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
members. Topic naming and compaction stay unchanged. Each attachment is a
directory of `prompt.json` plus the same `work.md`, `plan.md`, and `review.md`
files (`.txt` is also accepted). A missing file contributes nothing for that
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
up, and down. Listing stays available. Neither flag writes
`prompt.json` or `config.json`. Older `promptSet` and `promptAttachments`
keys in `config.json` are ignored and omitted the next time preferences are
saved. There is no migration tool.

## Model

`/model` changes the configured provider and model for the next idle
turn. Crystal `ChatRequest` has no model field; adapters bake the name
and sampling into client options, so the session rebuilds
`IStreamingChatClient` (and the compaction summarizer and Review
client that share it). The transcript stays the same conversation.

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
- Compaction is not run as part of the switch. The next turn uses the
  new context window.

## Approval

Every side-effect tool call is classified before invocation:

- Risk: Read, Write, Privileged, Forbidden.
- Authority: Workspace, OutsideWorkspace, Network, PrivilegedEscalation.
- Grant: Once, Session, Persistent.

Modes:

- Plan: no built-in edit, write, or bash. Workspace reads auto-execute.
  Reads, list, glob, and grep of paths outside the workspace ask the operator.
  When Skills is enabled, any path inside a Skills search directory
  (`skill` / `skills` trees) auto-executes as a workspace read. The
  comparison uses the final target of each symbolic link. External
  tools listed for Plan keep Write + Workspace and still go through
  approval.
- Default: Workspace Read auto-executes. Write, shell, and
  paths outside the workspace ask the operator.
- Edit: workspace file changes for built-in `write` and `edit`
  pass without review. Shell, external tools, and paths outside the
  workspace still ask.
- Review: workspace file changes for built-in `write` and `edit`
  pass without review, same as Edit. Another model checks
  each remaining side-effect call (Codex guardian-style), including
  bash, reads, list, glob, grep, write, and edit of paths outside the
  workspace, and external Write. Workspace reads and Skills search
  directories still
  auto-execute. A bounded transcript excerpt is attached: the first
  and latest user turns as authorization anchors, other user turns
  that fit, then recent assistant and tool evidence. A compaction
  summary stands in for folded user turns. Without that evidence the
  host asks the operator. Later user messages refine the task; a
  status question does not revoke earlier authorization. The reviewer
  returns `outcome` (allow / deny / ask), `risk_level` (low / medium
  / high), `user_authorization` (low / medium / high), and
  `rationale`. Allow executes. Deny becomes model-visible rejection
  text. Ask and Forbidden-allow fall back to the operator. Review is
  not a grant and is not full pass-through.
- Audit: the same reviewer and transcript rules as Review, but
  workspace `write` and `edit` are also checked. They do not auto-pass.
- Full: workspace-bounded, policy-allowed actions pass without review.
  That includes any loaded external tool whose classification stays
  Write + Workspace, not only built-in `write` / `edit`. Forbidden,
  Privileged, and outside-workspace paths never fully auto-pass.

The reviewing model is the session model unless `approvalModel` is
enabled in `config.json`. The object is `enabled`, `provider`, and
`model`. An omitted object means off. `enabled: false` keeps a stored
provider and model unused, and Review and Audit follow `/model` and the
work thinking gear. While enabled, review calls use a separate client
and that provider's credentials. `/model` and the work thinking gear do
not apply. The reviewer uses the approval model's default thinking
gear. Compaction stays on the session model. Review usage is not added
to the work transcript. A missing credential, a missing catalog model,
or a saved switch that names a model the catalog does not contain fails
instead of falling back to the session model. `/approval model` shows
the switch, turns it on or off, or sets a model or `provider model` and
turns it on. `/status` adds an Approval model row only while the switch
is on. The status bar does not.

`crystal run` can set the switch for one process with
`--approval-model on|off`, `--approval-provider`, and
`--approval-model-id`. Those flags are not written to `config.json`.
Passing a provider or model id turns the switch on for that process.
`--approval-model off` keeps any stored names unused and rejects a
provider or model id on the same command.

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

When a call auto-passes (policy, remembered grant, or review allow),
the shell prints a panel with Title Case fields: Status, Reason, Risk,
and Authority, plus the classifier summary. A review allow also prints
Outcome, review Risk, Authority, and rationale.

Persistent grants are stored in `~/.crystal/permissions.json`.

## Compaction

Crystal does not reduce context. When estimated transcript size or the
last model-round usage crosses the lesser of the configured fraction of
the model context window and the usable window (context minus reserved
output), the host:

1. Clears old tool results outside a protected recent band, when enough
   tokens would be freed.
2. Asks the model for one structured summary of older turns, folding any
   previous summary, and keeps a recent tail verbatim. That summary
   request uses the same session retry policy as a model round.
3. Stops if the summary request itself cannot fit or the summarizer
   returns nothing and prune did not help. The turn does not retry
   compaction in a loop.

CTX percent uses the last model round's provider usage while a round is
streaming, then a local transcript estimate after each tool batch until the
next round reports. It is not the sum of rounds in
the turn. The status bar can show 100% when usage meets or exceeds the
window. Compaction does not block the session loop: the progress row
stays on `Compacting` and the composer remains usable.

`/compact` (alias `/summarize`) runs immediately and does not wait for
the threshold. When the retained tail already holds every turn, it still
summarizes everything before the latest user turn. A session with nothing
before that turn prints `Nothing earlier to compact`. It is refused while
a turn is running. User and assistant text in the folded head are replaced
by the summary; they are not kept beside it. The screen keeps the earlier
lines. When `showCompactionSummary` is on (the default; omitted from
`config.json` when on, `false` when off), the stored summary is printed
under those lines in a rounded panel titled `Earlier context`. Automatic
compaction does not print it.

Sessions are written to `~/.crystal/sessions/<id>.json` after each
completed turn, after a successful `/compact`, and on an orderly exit
when the transcript has a user message or a compaction summary. The
file stores the compacted model transcript (live system prompt, one
summary, recent tail, and reasoning items with their readable text and
opaque provider state), the last usage snapshot, and cumulative provider usage.
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
the live system prompt is refreshed from that directory's Plan/Work text;
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
are `linux-x64`, `linux-arm64`, `macos-arm64`, and `windows-x64`. The platform
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
  prompt-history.jsonl
  credentials.json
  permissions.json
  trusted.json
  instructions.md
  prompts/work.md
  prompts/plan.md
  prompts/review.md
  prompts/topic.md
  promptsets/<name>/prompt.json
  promptsets/<name>/work.md
  promptsets/<name>/plan.md
  promptsets/<name>/review.md
  prompt-attachments/<name>/prompt.json
  prompt-attachments/<name>/work.md
  prompt-attachments/<name>/plan.md
  prompt-attachments/<name>/review.md
  skill/<name>/SKILL.md
  skills/<name>/SKILL.md
  tools/<directory>/tools.json
  sessions/<id>.json
  media/<sha256>
  logs/
  plugins/
  space/
```

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

`bashTimeoutSeconds` is omitted when it is the 120 second default. `null` and
`"unlimited"` both mean no per-command timer.

`providers.json` stores the provider catalog as a root object keyed
by provider name, with an array for multiple protocols under one name. The
catalog overlays built-in definitions. When `providers.json` exists, it takes
precedence over legacy `config.json.providers`; otherwise the legacy field is
read. Saving preferences copies legacy provider definitions to `providers.json`
if needed, then preserves the legacy field in `config.json` so an older running
version can continue reading it. Preference saves do not rewrite an existing
`providers.json`. Duplicate model ids across protocols under one provider are
rejected. Editing provider definitions in the TUI is deferred product work.
The catalog changes by editing this file, then restarting.

Project overlay (wins over home for named prompts, prompt attachments,
Crystal skills, and tool sets of the same directory name):

```text
<workspace>/.crystal/
  instructions.md
  prompts/work.md
  prompts/plan.md
  prompts/review.md
  prompts/topic.md
  prompt-attachments/<name>/prompt.json
  prompt-attachments/<name>/work.md
  prompt-attachments/<name>/plan.md
  prompt-attachments/<name>/review.md
  skill/<name>/SKILL.md
  skills/<name>/SKILL.md
  tools/<directory>/tools.json
<workspace>/.crystal.md
```

`config.json` is not part of the project overlay. Operator preferences load
from the Home `config.json` only; workspace-level configuration is deferred
product work.

Overlay is built-in default, then `~/.crystal`, then the project
`.crystal`. Named prompt files replace the built-in Work, Plan, or
Review system text.

Built-in Work and Plan identify the assistant as Crystal Code. Operators
who replace those files choose their own identity.

`instructions.md` and `.crystal.md` are appended under
"Workspace instructions" on Work and Plan only. Review does not receive
that instruction block. Enabled prompt attachments can still append to
Review. Files may be `.md`
or `.txt`. Empty files are treated as missing. The host never writes
prompt files. The host appends a non-overlayable `<env>` block between
the Work or Plan body and those instructions. When Skills is enabled,
available-skill guidance is appended after the env block.

Skills are OpenCode-compatible `SKILL.md` folders. They are loaded
on demand through the `skill` tool. Available-skill guidance lists
name and description only; it does not include absolute paths. When
Skills is enabled, `read`, `list`, glob, and grep of any path inside a Skills
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
- Project: walk from the workspace up to the git root. The first
  matching name wins (`AGENTS.md`, then `CLAUDE.md`, then
  `CONTEXT.md`). Every file of that name on the walk is appended.
  `CLAUDE.md` is used only when no `AGENTS.md` exists on the walk.

`credentials.json` is the persistent API-key store. Values are plain text,
keyed by provider name, and the file is created with owner-only
permissions. Process environment variables override those file credentials.

Operator tool sets live under `tools/`. A project directory of the same
name replaces the home set as a whole. `tools.json` field `enabled`
(default `true`) omits a set without deleting it. `config.json` field
`externalTools` enables discovery (default `true`). See
[docs/external-tools.md](docs/external-tools.md).
An unreadable or invalid project manifest with the same directory name
also hides the home set. `timeoutSeconds` defaults to 120 for both runners;
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
latest request token breakdown and adds Activity and Tools cards for session
identity and start time, invocation counters, queue and todo counts, and
tool-catalog totals. The persistent chrome remains the smallest live summary.
`/status` labels cumulative values as session usage and reserves the latest
request breakdown for `/status full`; its output does not depend on status-line
customization.

Operator plugins live under `plugins/`. A project directory of the same
name replaces the home plugin as a whole. `plugin.json` field `enabled`
(default `true`) omits a plugin without deleting it. `config.json` field
`plugins` enables discovery (default `true`). See
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
gear is a host setting. The API key for `openrouter` belongs in
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
thinking and which Crystal effort names it accepts (`minimal`, `low`,
`medium`, `high`, `maximum`). `max` is accepted as `maximum`. Built-in
DeepSeek V4 models enable thinking with `low`, `high`, and `maximum`. An empty `thinkingEfforts`
list is on/off only. `thinkingCanDisable` defaults to true. Set it to
false when the model rejects a request to turn thinking off. Built-in
Gemini 3 models do. `/thinking` then omits Off. A stored Off gear falls
back to the provider default and the status bar shows `Think Default`.
An explicit `/thinking off` is refused.

`thinkingEffort` is the operator choice: `default`, `off` (`none` is
the same), or a Crystal effort name. It is not stored on the model. `/thinking` (alias
`/think`) cycles the gear or sets one by name. `/model` never
fails because of thinking: if the model does not support thinking, requests omit reasoning
hints; if the stored gear is not in that model's list, the request
uses the provider default and the stored choice is unchanged.

`skills` enables the `skill` tool and available-skill guidance
(default `true`). Set it to `false` to disable skill discovery.

`externalTools` enables operator tool set discovery (default `true`).
Set it to `false` to skip `tools/` manifests.

`plugins` enables operator plugin discovery (default `true`).
Set it to `false` to skip `plugins/` manifests. See
[docs/plugins.md](docs/plugins.md).

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
deferred. There is no global context window.

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

A front end calls `CodingSession`: `Create`, `StartAsync`, `SubmitAsync`
(slash commands, follow-ups while a turn runs, interrupts, and turn starts;
returns true when the operator asked to quit), `CompleteTurnAsync` once
`TurnTask` finishes, `FinishTurnAsync` to collect a turn before exit,
`TryInterrupt`, `TogglePlan`, `SetVerbose`, `NotifyDraftChanged`, `Enqueue`,
`PasteClipboardImageAsync`, and `Close`. `TurnActive`, `TurnTask`,
`PlanMode`, and `SessionId` report state. The usual turn is:
`SubmitAsync`, await `TurnTask`, then `CompleteTurnAsync`.

Events are immutable sealed records, one type per file under `Events`:

- Lifecycle and chrome: `SessionStarted`, `ChromeChanged`,
  `PreferencesChanged`, `SlashCommandsChanged`, `PromptHistoryLoaded`,
  `ImageHistoryInvalidated`, `ActivityChanged`.
- Turn: `UserMessageSent`, `TurnStarted`, `StreamReceived` (carries Crystal's
  `ChatStreamEvent`), `ToolCallsIssued`, `ToolResultsReceived`,
  `ModelRoundClosed`, `RetryScheduled`, `UsageChanged`, `TurnFinished`,
  `QueueChanged`.
- Conversation: `HistoryReplayed`, `ConversationCleared`, `TodosChanged`.
- Text and reports: `NoteWritten`, `ErrorWritten`, `HelpRequested`,
  `StatusReported`, `ToolsListed`.

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

`crystal run` is the headless front end in this executable, not a second
assembly. It reads one task from an argument or from stdin, applies
process-only overrides, starts one user turn, and exits. The interactive
`crystal` and `crystal space` commands apply the same session overrides:
approval, the approval model, Plan or Work, thinking, prompt set, prompt
attachments, skills, external tools, plugins, and turn quotas. Overrides are not
written to `config.json`. That includes the approval-model switch
(`--approval-model`, `--approval-provider`, `--approval-model-id`).
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
dismisses. Session choice is declined. Review and Audit still call the
reviewing model: allow executes the tool, and deny returns the reviewer's
reason. Plan does not offer write, edit, or bash. The approval policy
also rejects those side effects when a catalog still contains them.

`--format default` writes a readable trace. Each model round prints thinking
text only when `--show-thinking` is set, then the assistant reply, then each
tool beside its own result. With `--show-thinking`, the reply is labeled
`[Assistant]`. `read`, `list`, `glob`, `grep`, and other successful
tools keep a short head excerpt and an omitted-line count. `edit` and `write`
keep their result. `bash` keeps the command, the exit status, and a short
tail, with a longer tail when the command fails. Other failures keep the
result text. Labels use square brackets, exit status uses parentheses, and
omitted-line markers use angle brackets. `--format json` writes one JSON object per
line, with `type`, a UTC `timestamp`, and `sessionID`, and keeps the full
tool output. Event types are
`step_start`, `text`, `reasoning`, `tool_use`, `error`, `note`, `retry`,
`step_finish`, `stopped`, and `session`. Thinking text is omitted unless
`--show-thinking` is set. The process then prints the saved session id.
Exit 0 means the turn completed and no operator prompt was denied or
dismissed. Exit 1 is an
invalid command, configuration, credential, workspace, or prompt set. Exit
2 is a failed model request. Exit 3 is a model-call, tool-call, or duration
budget, or context overflow. Exit 4 is a finish that was not a failure,
budget stop, or interrupt, after an operator denial or a dismissed question.
Exit 5 is an interrupt. A failure, budget stop, or interrupt outranks a
denial.

## Display

CrystalCode.Display is the TUI host. Spectre.Console supplies markup,
color, panels, grids, rules, and padding as an offline rasterizer.
Process startup sets console output to UTF-8 without a BOM and turns
Spectre's Unicode output on, then restores the previous output encoding
when the process exits. Console input encoding is left alone. Files,
prompts, and provider bodies are already UTF-8. The progress spinner is
braille, so a Windows OEM code page would replace each frame with `?`.
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

The status bar shows approval, thinking (when the selected model
supports it: `Think Off`, or `Think` plus the resolved gear when
thinking is on), the non-default prompt set as `Prompt <name>`, model,
workspace, the latest request's context percent (`CTX`), cumulative
token counts (`IN` / `OUT`), and, when the bar has room, a cumulative
Title Case total (`783k Total`). During a turn the status bar updates `CTX`, `IN`, and `OUT` after each
model round and each tool batch: provider usage as it arrives, then a local
transcript estimate after tools until the next round reports. It also
shows tool count (`Tool` / `Tools`), and
elapsed time. When the session has todos, a pinned `Todos` bar sits
above the progress row and status bar. The existing adaptive status bar is the
default. Operators may independently enable an ordered custom status line
through `customStatusLine`, `statusLine`, or `/statusline`. Custom token fields
name their scope explicitly as context, request, or session usage. Disabling
the custom line returns to the existing adaptive rendering path.
Each row is a checkbox colored
by status: pending chrome, in progress accent, completed ok, cancelled
muted. The bar shows the first four items and a `+N more` line when
the list is longer; `/todos` (alias `/todo`) prints the full untruncated
list in the transcript. The transcript Result card uses the same colors for
`- [ ]` / `- [~]` / `- [x]` / `- [-]` lines so they are not painted as
diff removals. Named chrome labels are Title Case. Short status
abbreviations are uppercase. Mode is Plan or Work on the
composer prompt and is not repeated on the status bar. While a turn
runs, a progress row sits directly above the status bar
(`Waiting For Model`, `Thinking`, `Writing`, `Running Command`,
`Awaiting Approval`, `Reviewing`, `Waiting For Answer`,
`Compacting`, `Retrying In Ns (Attempt K)`). The retry caption counts
remaining wait down. The same row shows
`Loading Tools` while operator tool
sets are discovered at session start and on `/cd` or `/space`, after the frame is
up, so a slow assembly load does not leave a blank terminal. The
caption is prefixed with a one-cell spinner
that advances while the turn is live, plus the current activity
elapsed time (`5s`, `2m18s`). When `estimatedTokens` is on, Thinking
and Writing also show a live estimate (`~1.2k Tokens`) after elapsed
time. Elapsed resets when the progress
caption changes. It is independent of the
status-bar activity bullet (`• Bash`). The row is omitted when
idle. Assistant text is
rendered as markdown while it streams and after it commits (headings,
lists, fenced code with a dim code background, inline code and bold).
User, thinking, tool, and result blocks are rounded panels. A live turn
writes a Tool card when the model round closes with tool calls, before
those calls execute, using the same one-line summary as session replay.
Tool names in chrome and cards are Title Case; stream name chunks are
coalesced per tool call so a repeated snapshot does not become
`ReadReadRead` and sequential calls never concatenate. Tool and bash
result panels honor independent verbose toggles (`verboseTools` and
`verboseCommands` in `config.json`, default on). When tool verbose is
off, read/search/skill result panels are omitted while the Tool card
remains; `edit` and `write` results always stay visible. When command
verbose is off, bash results collapse to a hidden-line hint plus the
last output line. Auto-pass approval cards honor `verboseApprovals`
(default on). When it is off, those cards are omitted, including review
allow cards already in the transcript; turning it back on shows them
again. The ask overlay still appears. Ctrl+O toggles tool verbose and
Ctrl+G toggles command verbose when the composer is empty; `/verbose`
shows or changes the same settings (`tools`, `commands`, `approvals`,
`thinking`, `on`, `off`). Thinking panels honor `verboseThinking`
(default on). When it is off, live and committed Thinking cards are
omitted, including cards restored with a session; turning it back on
shows the stored text again. Inline output, used when the alternate
screen is unavailable, applies the same switches to restored thinking,
tool results, and approval text. The progress row, the thinking gear, and
export stay. `crystal run --show-thinking` remains a separate one-shot
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
cards for `edit` and `write` show a capped `+` / `-` preview
of `old_string` / `new_string` or `contents`. Overlay keys share the
session frame loop, so transcript scroll and resize still work while a
prompt is up. Ask overlays use the same card; `Y` / `S` / `A` / `N` map
Once / Session / Always / Deny. The follow-up queue is a `Queued` panel
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
newline. Tab toggles Plan/Work or completes a `/` command. Shift+Tab also toggles Plan/Work. Chrome labels are Plan, Work, Review, Default, Edit, Audit, and Full.
When the slash picker is open, plain Up/Down moves its selection without
resetting it and scrolls the visible candidate window to keep that selection
on screen. Tab accepts the selected completion. Enter accepts a partial
selection first; when the composer already contains that complete command or
argument, Enter submits normally.
Status abbreviations are CTX, IN, and OUT. The status
bar includes a queued count while follow-ups wait. Auto-pass prints a
panel with Status, Reason, Risk, Authority, and, for review, Outcome
plus rationale, while `verboseApprovals` is on. Reasoning streams into the
transcript while `verboseThinking` is on. Built-in slash verbs live in `SlashCatalog` and include
aliases (`/new` is `/clear`, `/continue` is `/resume`,
`/q` and `/exit` are `/quit`, `/think` is `/thinking`, `/summarize` is
`/compact`, `/todo` is `/todos`, `/prompts` is `/promptset`,
`/verbose` toggles tool, command, approval, or thinking detail). `/export`
writes markdown or json for the current conversation; `/prompts export`
writes built-in prompt templates. Export roots default to
`~/.crystal/exports/` (`exportDirectory` omitted or `home`) and are
configurable through `exportDirectory` in
`config.json`. A slash picker appears while the prompt
is a command prefix. After a verb that takes an argument
(`/thinking`, `/approval`, `/model`, `/tokens`, `/verbose`), Tab also completes the argument.
`/model` completes current-provider models, then a provider name, then
that provider's models. `/export` completes the format, optional system flag,
and the system flag after an explicit path. `/prompts export` offers directory
examples while Enter on the optional argument boundary still submits with the
default directory. Ctrl+O and Ctrl+G toggle verbose tool and command output when the composer is
empty. Plain Up/Down move through the composer's visual rows, including
wrapped rows. At the first or last row they navigate submitted prompt
history; Up on an empty prompt recalls the latest entry, and Down past
the newest entry restores the unsent draft and its cursor. The slash
picker owns plain Up/Down while open. Submitted text-only history is
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
drained without waiting; other mouse reports are swallowed. The wheel is
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
Built-in tools and all six wire protocol adapters register through that
table. `PluginRegistry` does not load assemblies from disk.

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
temperature, top-p, image input, and thinking gear. `Review.Independent`
is true only when review uses its own model. While it is false, review
uses the session model and a stored selection is omitted. The view has no
API key, endpoint, organization, or project. A plugin that implements
`IPluginModelClient` also receives `AttachClients`. The host names that
plugin when it loads. Those clients are new instances on the same provider
and model, kept apart from the instances inside a turn or a review.
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

Operator tool sets are not plugins. They are discovered from `tools/` and
wrapped by `ExternalCatalog`. An exec child starts in the workspace root
and receives `CRYSTAL_WORKSPACE`, `CRYSTAL_SESSION`, and `CRYSTAL_APPROVAL`
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
root, session id, approval mode). Other tools keep the original
`InvokeAsync`. Native multimodal tools join
the active catalog only for an image-capable model and provider. The external-tool loader does not implement `CrystalCode.Plugins.IPlugin`
and does not scan `plugins/`.

Environment variables:

- `CRYSTAL_HOME` overrides the data directory.
- `DEEPSEEK_API_KEY`, `OPENAI_API_KEY`, `GEMINI_API_KEY`, and `CRYSTAL_API_KEY` override
  `credentials.json` for that process. A provider-specific variable wins over
  `CRYSTAL_API_KEY`. The persistent store is `credentials.json`.
