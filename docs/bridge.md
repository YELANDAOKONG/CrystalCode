# crystal bridge

Status: all four phases are in this build (see Phases and Scope in this
build). `crystal bridge`, `CrystalCode.Bridge`, and `CrystalCode.Bridge.Tests`
exist. Manual checks against Zed and the ACP Test Compatibility Kit are still
open.

Bridge is the Agent Client Protocol (ACP) front end. An editor such as Zed,
or any other ACP client, starts `crystal bridge` as a subprocess and drives
one session over stdio. Bridge is a third operator surface beside the
terminal and `crystal server`. It is not `crystal server`, not `crystal run`,
not an engine protocol, and not child Agents.

The goal is maximum reach: every engine capability a front end can use is
reachable through Bridge, through standard ACP where ACP has a shape for it,
and through `_meta` and `_crystal/*` extension methods where it does not.
Approval, trust, the workspace fence, `ToolInvocationPolicy`, prompts, and
storage stay where they are. Bridge only carries their questions and
results.

## Decisions

- One process hosts one active `CodingSession`. `session/new`,
  `session/load`, `session/resume`, and `session/fork` save the active
  session and replace it, as `/resume` does. A request that names another
  session id returns `-32002`. An editor that wants parallel sessions starts
  more processes.
- `additionalDirectories` become session `write` rules, the same as
  `--allow-write`. Each request's list is the complete additional-root set
  for the resulting session, as ACP requires (see Additional roots). The
  rules are process-only and never written to `access.json`.
- Bridge does not call the client's `fs/*` or `terminal/*` methods. Tools run
  in this process under the host fence, budgets, and timeouts. Editors follow
  the agent through tool call `locations` and `diff` content.
- ACP protocol version 1. That integer is ACP's own negotiation field, not a
  version of a CrystalCode protocol. v2 is a draft and is not implemented.

## Deviations from ACP

Bridge follows the stable v1 schema. Where it goes further, the behavior is
listed here. Bridge never adds a field at the root of a spec type: its own
capabilities are advertised under `agentCapabilities._meta.crystal`, and the
client capabilities it reads beyond the schema come only from
`clientCapabilities._meta.crystal`. A non-schema update variant, method, or
session id is sent only after the client opted in there. A client that
never opts in sees plain ACP.

| Deviation | ACP | Bridge | Advertised as |
| :--- | :--- | :--- | :--- |
| `session/load` and `session/resume` with a `cwd` that differs from the saved workspace | `cwd` is fixed after setup; `additionalDirectories` may differ only when the request `cwd` matches the session's `cwd` | The session moves to the request `cwd` after trust, as `/resume <path>` does, the live system prompt is refreshed for that directory, and the request's `additionalDirectories` apply. `session/list` then reports the new `cwd`. | `crystal.loadAcrossCwd: true` |
| `/cd <path>`, `/space`, and `/access` inside a session | `cwd` is immutable and additional roots have no mid-session mutation | The slash commands still work. The change is reported in `session_info_update._meta.crystal` (`workspace`, `additionalDirectories`). A client that relies on a fixed `cwd` can avoid them; `session/list` reports the current values. | `crystal.mutableWorkspace: true` |
| Session fork | Not in the stable schema: no `session/fork` method and no `sessionCapabilities.fork`; only the session fork RFD | `_crystal/session/fork` is the extension method. `session/fork` is also answered with the RFD's request and result shape for clients that implement the RFD. `sessionCapabilities.fork` is not sent. | `crystal.fork: "rfd"` |
| Subagent sessions | Not in the stable schema: `ClientSessionCapabilities` has only `compaction`, `configOptions`, and `notices`, and there is no `subagent_update` variant; only the subagent sessions RFD | Read from `clientCapabilities._meta.crystal.subagents: {}`. Only then does Bridge send `subagent_update` on the parent and child traffic (`session/update`, `session/request_permission`, `elicitation/create`) under child session ids, in the RFD's shapes. A root `clientCapabilities.subagents` is not read, because the RFD may still change. | `crystal.subagents: "rfd"` |
| `_crystal/*` notifications | ACP allows extension notifications; a client may ignore them | Sent only after `clientCapabilities._meta.crystal.extensions: {}`. `_crystal/*` requests are answered without it, because only a client that knows them sends them. | `crystal.eventKinds` |

## Projects and ownership

| Project | Owns |
| :--- | :--- |
| `CrystalCode.Bridge` | `Transport` (newline-delimited JSON-RPC 2.0 over streams, request ids, `$/cancel_request`, error codes, `ProtocolLog` for the `--debug` traffic log), `Wire` (every field written explicitly with `Utf8JsonWriter`: `SessionUpdates` maps `SessionEvent` and Crystal stream events to `session/update` and `_crystal/*` notifications and replays the archive; `ChildUpdates` for children; `ToolCallWire`, `PermissionWire`, `TurnWire`, `ConfigWire` for config options and legacy modes, `FormWire` for elicitation forms, `McpServerWire` for client MCP servers, `SessionList`, `CrystalWire` for extension values with the server's field names), `Prompts` (content blocks to submitted text), and `Hosting` (`CrystalBridge` entry, `BridgeAgent` methods, `BridgeSession` with the live `CodingSession`, `SessionRunner`, front-end contracts, and turn tracking; `LaunchTrustPrompt` for trust before a session exists; `Extensions` for `_crystal/*` methods and `ReportCapture` for their report results). References `CrystalCode.Engine` only. |
| `CrystalCode.Bridge.Tests` | An in-memory duplex stream driven by a scripted model, the parity guards, the secret-leak test, and the dependency guard. |
| `CrystalCode` | `Commands/BridgeCommand` parses the command line, rebinds console output, maps signals, and hands the raw stdin and stdout streams to `CrystalCode.Bridge`. |

`CrystalCode.Bridge` does not reference `CrystalCode.Display`,
`CrystalCode.Server`, Spectre.Console, Terminal.Gui, the executable, or
`System.Console`. It adds no package: JSON-RPC and the wire use
`System.Text.Json` from the shared framework. No third-party ACP SDK is used.
It holds no product logic and never serializes an engine record by
reflection.

## Command line

```text
crystal bridge [session launch options] [-w|--workspace <PATH>] [--space]
               [--config-options <IDS> | --default-config-options | --all-config-options]
               [--workspace-trust <on|off>] [--debug [FILE]]
```

- Session launch options are the set `crystal` accepts. They shape this
  process only and are not written to `config.json` or `prompt.json`. The
  launch provider and model may be saved by a later `/model`, as on the
  terminal.
- `--workspace` and `--space` only set the fallback for a session request
  whose `cwd` is missing. ACP always sends `cwd`, so in practice the client
  chooses the workspace.
- `--config-options`, `--default-config-options`, and `--all-config-options`
  choose which session options the editor sees as config options (see
  Configuration). They are mutually exclusive, apply to this process only,
  and are never written to `config.json`. A conflict or an unknown option id
  stops startup with a message on stderr.
- `--workspace-trust <on|off>` sets the trust check for this process,
  ahead of `bridge.workspaceTrust` in `config.json` (see Sessions). It is not
  written to `config.json`.
- `--debug [FILE]` logs every JSON-RPC message in both directions, one line
  each with a UTC timestamp and the direction (`client -> bridge`,
  `bridge -> client`). A bare `--debug` writes to stderr, which editors
  usually collect in their agent log. With `FILE`, the log is appended to that
  file (a relative path is resolved against the process directory) and stderr
  stays quiet; a file that cannot be opened stops startup with a message on
  stderr. It applies to this process only and is never written to
  `config.json`. The log is a diagnostic aid, not a transcript: the values of
  `mcpServers`, `env`, `headers`, and credential-like fields (`apiKey`,
  `token`, `authorization`, `password`, `secret`, `credentials`) are replaced
  with `[redacted]` at any depth, strings longer than 256 characters (image
  data, file text, tool output) are cut with their length, and a line that is
  not valid JSON is logged as a length only. Prompt text and tool arguments
  that fit are logged as sent, so treat the file like the conversation.
- No secret is a flag. Provider keys come from Home and the environment.
- Editors configure it as `{"command": "crystal", "args": ["bridge"]}`.

## Transport

- Messages are UTF-8 JSON-RPC 2.0 objects, one per line, on stdin and
  stdout. Batches are rejected with `-32600`.
- Stdout belongs to the protocol. Before anything else runs, the command
  captures the raw stdout stream for the transport and points `Console.Out`
  at stderr, so a plugin or tool that writes to the console cannot corrupt
  the stream. Diagnostics go to stderr in plain English.
- Requests are dispatched in order through one reader. Long requests
  (`session/prompt`, `session/load`) run concurrently with later requests so
  `session/cancel`, permission answers, and extension calls stay live.
- `$/cancel_request` cancels a running Bridge request and answers it with
  `-32800`. For `session/prompt` it also interrupts the turn, as
  `session/cancel` does, and answers once the turn has stopped. Bridge sends
  `$/cancel_request` for its own outstanding
  permission and elicitation requests when the turn that raised them ends.
- Errors use only codes whose ACP meaning fits:

  | Code | ACP meaning | Bridge use |
  | :--- | :--- | :--- |
  | `-32700` | Parse error | Malformed JSON |
  | `-32600` | Invalid request | Not a JSON-RPC object, or a batch |
  | `-32601` | Method not found | Unknown method, including an unlisted `_crystal/*` |
  | `-32602` | Invalid params | Missing or mistyped fields, a relative path, an unsupported content block, a config value not offered |
  | `-32002` | Resource not found | An unknown or inactive session id, an unknown saved session on load, resume, or fork, an unknown image number or child id |
  | `-32800` | Request cancelled | `$/cancel_request` and shutdown |
  | `-32603` | Internal error, implementation-defined | Every engine refusal and failure: a refused launch (credentials, trust, invalid prompt set), a command refused while busy, a failed turn, context overflow |

  `-32000` (Authentication required) is never sent. Bridge has no
  `authMethods`, and a missing provider credential is a launch refusal
  (`-32603`), not an authentication request, so a client never starts a
  login flow for it. No other code in the `-32000` to `-32099` range is
  used, because ACP reserves that range for its own errors.
- `error.message` is the engine's English text, one short sentence.
  `error.data.crystal` carries the detail: `kind` (`launch_refused`,
  `command_refused`, `turn_failed`, `context_overflow`, `trust_refused`,
  `internal`), the engine stop reason when a turn ended, and the turn counts
  and usage. No error text or data contains a secret.
- Stdin closing starts shutdown. SIGINT, SIGTERM, and SIGHUP do too.
  Shutdown interrupts the turn, collects it without starting queued work,
  cancels pending interactions, answers running requests with `-32800`, runs
  `CloseAsync`, and exits with code 0. A second signal ends the process at
  once.

## Initialization

`initialize` returns:

```json
{
  "protocolVersion": 1,
  "agentInfo": {"name": "crystal-code", "title": "Crystal Code", "version": "<build identity>"},
  "authMethods": [],
  "agentCapabilities": {
    "loadSession": true,
    "promptCapabilities": {"image": true, "audio": false, "embeddedContext": true},
    "mcpCapabilities": {"http": true, "sse": true},
    "sessionCapabilities": {
      "list": {}, "resume": {}, "close": {}, "additionalDirectories": {}
    },
    "_meta": {"crystal": {
      "extensions": ["_crystal/..."], "eventKinds": ["..."],
      "loadAcrossCwd": true, "mutableWorkspace": true,
      "fork": "rfd", "subagents": "rfd"
    }}
  }
}
```

`agentInfo.version` is the first 12 characters of the
product commit, or `dev` for a build without one. `protocolVersion` is
always 1, whatever the client sent.

- `image` and `embeddedContext` are always true. Bridge accepts every block
  it advertises and never fails a prompt because the current model cannot
  use one; see Prompt content. `audio` is false because the engine has no
  audio input, so clients do not send it.
- Fork and subagents are RFDs, not stable ACP, so they appear only under
  `_meta.crystal` (see Deviations). When either reaches the stable schema,
  the change that adopts it moves it to the spec field and updates this
  document.
- `_meta.crystal.extensions` lists every `_crystal/*` method this build
  answers, and `eventKinds` every `_crystal/*` notification. Both follow the
  build and carry no version.
- `authenticate` and `logout` are not advertised. Credentials stay in
  `credentials.json` and the environment. A session request with no usable
  credential fails with `-32603`, `kind` `launch_refused`, and the launcher's
  English error.

Client capabilities Bridge reads:

| Client capability | Effect |
| :--- | :--- |
| `elicitation.form` | Enables the `question` tool, MCP elicitation, trust, and session and child choices as forms. Without it they fall back as described under Interactions. |
| `session.notices` | Notes, errors, and retries become `notice`. Without it, only errors and turn-level failures become agent text. |
| `session.compaction` | Compaction becomes `compaction_update` and `compaction_summary_chunk`. Without it, a displayed summary becomes agent text. |
| `session.configOptions.boolean` | Switches are sent as boolean options. Without it they are two-value selects. |
| `_meta.crystal.subagents` (not ACP) | Child sessions are exposed as restricted sessions in the subagent RFD shapes. Without it, child progress goes into the parent `task` tool call and child prompts use the parent session id. See Deviations. |
| `_meta.crystal.extensions` (not ACP) | `_crystal/*` notifications are sent. Without it none are. See Deviations. |
| `fs`, `terminal`, `auth.terminal` | Read and ignored. |

## Sessions

| ACP | Engine |
| :--- | :--- |
| `session/new {cwd, mcpServers, additionalDirectories}` | `SessionLauncher.PrepareAsync` with `Workspace = cwd`, then `CreateSession` and `StartAsync`. Returns `sessionId`, `configOptions`, and `modes`. |
| `session/load {sessionId, cwd, ...}` | Prepare with `Resume = true`, `ResumeTarget = sessionId`. Replays the archive (see Replay), then returns `configOptions` and `modes`. An unknown saved session is `-32002`. |
| `session/resume {sessionId, cwd, ...}` | Same restore without replay: the engine's start replay is dropped. |
| `_crystal/session/fork {sessionId, cwd, ...}`, and RFD `session/fork` (not ACP) | `/fork <id>` semantics: a new id from that saved session, in `cwd`. The source file is not changed. |
| `session/list {cwd, cursor}` | `SessionStore.List(workspaceRoot)`, newest first. `title` is the summary preview, `updatedAt` the update time, `additionalDirectories` the saved outside-workspace roots, `_meta.crystal` carries `mode`, `planMode`, `userTurns`, and `createdAt`. Pages of 50 with an opaque cursor; an invalid cursor is `-32602`. Omitting `cwd` lists every workspace. |
| `session/close {sessionId}` | Interrupt, collect, `CloseAsync`. The process stays up for another `session/new`. |
| `session/set_mode` | The `mode` option's `/mode <id>` command. A mode not offered is `-32602`. |
| `session/set_config_option` | See Configuration. |
| `session/cancel` | `TryInterrupt`; pending permission and elicitation requests for that turn are cancelled and treated as deny. |

- `sessionId` is the engine session id. `/clear` inside a session makes a
  new engine id. Bridge keeps the ACP id stable for the connection and
  reports the new engine id in `session_info_update._meta.crystal.engineSessionId`.
  `session/load` accepts either id.
- `cwd` must be absolute. A `cwd` on load or resume that differs from the
  saved workspace is a deviation from ACP (see Deviations): the session
  enters `cwd`, as `/resume <path>` does after trust.
- Workspace trust is off by default for `crystal bridge`, because the editor
  chooses the directory and has already opened it. The `bridge` section of
  `config.json` turns it on with `"workspaceTrust": true`, and
  `--workspace-trust on|off` overrides that for one process. The terminal's
  top-level `workspaceTrust` does not apply to Bridge. While it is off, no
  trust question is asked, nothing is recorded in `trusted.json`, and
  `/cd`, `/space`, and `/resume <path>` skip the check too. Project tool
  sets and project MCP servers then start without it (see
  [mcp.md](mcp.md)). The rest of this item applies while it is on: trust uses
  `SessionTrustCheck.Prompt`. The question goes to the client as a form (see Interactions). A client without
  forms gets `-32603`, `kind` `trust_refused`, naming the directory and
  saying to run `crystal` there once, or to turn the switch off. The
  operator space is trusted as elsewhere.
- `mcpServers` become session-level MCP definitions (`McpServerSource.Session`),
  which replace a Home or Project server of the same name. They use the same
  MCP client, approval, `mcpToolApproval` (Session follows the Project
  choice), timeouts, and notes as local servers. They are not saved in the
  session file; ACP resends them on load and resume. Their command,
  arguments, environment, headers, and URL never appear in an event, a note,
  `/mcp`, a log, or an error.
- `additionalDirectories`: see Additional roots.
- `session_info_update` is sent after the first user message and on each
  save, with `title` from the session preview and `updatedAt`.

### Additional roots

ACP's root set is `[cwd, ...additionalDirectories]`, and on load and resume
the request carries the complete intended list; omitting it or sending `[]`
does not restore stored roots. Bridge follows that exactly for roots and
keeps CrystalCode's narrowing rules, because a narrowing rule is not a root:

- Roots are directory rules outside `cwd`. After `session/new`,
  `session/load`, `session/resume`, or `session/fork`, the session's roots
  are exactly the request's `additionalDirectories`, each as a `write` rule,
  in request order. Saved outside rules from the session file and
  `access.json` defaults outside `cwd` are not applied. Omitted or `[]`
  means no roots.
- Narrowing rules are `read` rules on `cwd` or a directory inside it. They
  only remove write access, so dropping them would widen access. `session/new`
  seeds them from `access.json` for that workspace; load and resume keep the
  saved ones. A request entry inside `cwd` is rejected with `-32602`, since
  it is already a root and cannot be widened over a narrowing rule.
- Each entry must be absolute and an existing directory; a credential path
  is rejected with `-32602`. Conflicting entries are rejected as launch
  flags are.
- The resulting rules are saved with the session as today, so a later
  terminal `crystal --resume` sees them. They are never written to
  `access.json`.
- Mid-session `/access` changes are the `mutableWorkspace` deviation. The
  next load or resume replaces the roots with the request list again.

### Replay

`session/load` streams the archive in order before it responds:
`user_message_chunk` for user text and images, `agent_thought_chunk` for
readable reasoning, `agent_message_chunk` for assistant text, `tool_call` and
`tool_call_update` (final status and content) for each call and result, for
each archived `## Earlier context` block a completed `compaction_update`
with its summary when the client has `session.compaction` and otherwise
`agent_message_chunk` with `_meta.crystal.replay = "compaction"`, and a final
`plan`, `usage_update`,
`available_commands_update`, and `config_option_update`. Opaque reasoning
state is never sent. Notices are not replayed.

A replayed image block carries the bytes from `TryGetImage`; an image the session no
longer holds is replayed as its plain `[Image #N]` text. `session/resume`
sends none of this, and a `/resume` inside a session replays as `load` does.

## Prompt turn

### Prompt content

Bridge accepts every content block it advertises, plus the baseline
`text` and `resource_link` that every ACP Agent must accept. A block is
given to the model in the richest form the engine supports for the current
model and session. A block the engine cannot use becomes a one-line mention
in the user message, never an error. Mention lines are fixed English text
authored in Bridge, not a prompt overlay. Names, titles, and URIs inside them
come from the client, so control characters and line breaks are removed and
each value is bounded; the model treats them as data like any user text.

| Block | Handling |
| :--- | :--- |
| `text` | Joined in order. Text that starts with `/` is a slash command, exactly as typed in the terminal. |
| `image` | `TryAttachImage` with the bytes and MIME type: signature check, 20 MiB limit, media store. Its trusted marker is inserted where the block was. When the current model has no image input, or the bytes fail validation, the block becomes the mention `[Image not attached: <reason>]` and a `notice` warning names the reason. |
| `resource` (text) | Inserted as a fenced block headed by its URI (or absolute path for `file://`) and MIME type. |
| `resource` (blob) | An image MIME type is handled as `image` above. A blob that decodes as UTF-8 text with a text-like MIME type is handled as a text resource. Any other blob becomes the mention `[Attached resource not shown: <uri> <mimeType>, <size> bytes, binary content is not passed to the model]`. |
| `resource_link` | See Links. Never refused. |
| `audio` | `-32602`. It is not advertised, and the engine has no audio input; a declared `audioInput` is not that path. |

### Links

A `resource_link` is mapped by its URI:

| URI | Handling |
| :--- | :--- |
| `file://` with a local absolute path, a regular image file, and a model with image input | Attached through the same path resolution and checks as `/attach` (see Engine changes). Its trusted marker replaces the block. If the attach fails (outside the paths `/attach` accepts, too large, not a valid image), it falls back to the next row. |
| `file://` with a local absolute path, any other file or a directory | The mention `[Linked file: <name> at <absolute path>]` or `[Linked directory: ...]`. The model reads it with its own tools. Approval is unchanged: workspace and session directories follow the current mode, other paths ask or go to review, and credential paths stay forbidden. A missing path is still mentioned, marked `(not found)`. |
| `file://` with a remote host, or a path that does not decode to an absolute local path | The mention for other schemes below. |
| Any other scheme (`https`, `http`, editor-specific, MCP resource URIs) | The mention `[Linked resource: <name> <uri> (<mimeType>) - Crystal Code did not open this link]`. Bridge does not fetch it and adds no fetch path. The model can reach it only through a tool the session already has, such as `read_mcp_resource` for a connected server that offers that URI, a plugin, or an external tool, under normal approval. There is no built-in web fetch (deferred in TODO.md). |

`title` is used in place of `name` when present. `size` is added to the
mention when known. `description` is not sent to the model. The archive
stores the user message as the model saw it, so `session/load` replays a
link as its mention text or its image, not as the original block.

### Turn results

- The response returns `stopReason` when the turn ends:

| `TurnStopReason` | `stopReason` |
| :--- | :--- |
| `completed` | `end_turn` |
| `output_truncated` | `max_tokens` |
| `model_call_limit_reached`, `tool_call_limit_reached`, `duration_limit_reached` | `max_turn_requests` |
| `content_filtered` | `refusal` |
| `interrupted` | `cancelled` |
| `failed`, `context_overflow` | error `-32603`, `kind` `turn_failed` or `context_overflow`, with the engine text |

  Every response carries `_meta.crystal` with the engine stop reason,
  `modelCalls`, `toolCalls`, `usage`, `lastRequestUsage`, `cumulativeUsage`,
  and `contextWindow`.
- A slash command that does not start a turn returns `end_turn` once the
  command finishes. Its output arrives as updates first. A command the
  engine refuses (for example while busy, or an unknown command) is
  `-32603`, `kind` `command_refused`, with the engine's text. A command sent
  while a turn runs returns `end_turn` at once and follows the engine's busy
  rules. `/quit` closes the session; the next request for its id is `-32002`.
- A prompt sent while a turn runs is a follow-up through
  `SessionRunner`/`EnqueueAsync`, with the engine's queue semantics. Its
  response resolves when the turn that consumed it ends. `QueueChanged` is
  reported in `_meta`. `_crystal/queue/release` is the empty-Enter release.
- A cancelled turn always answers `cancelled`, never an error.

## Session updates

Every update carries `_meta.crystal.event` with the engine event name, so a
Crystal-aware client can tell sources apart. Child events also carry
`_meta.crystal.child` (`childSessionId`, `invocationId`).

| Engine | Update |
| :--- | :--- |
| `UserMessageSent` | Not echoed during a live turn; the client shows the prompt it sent. Replay on `session/load` sends `user_message_chunk` with text and image blocks, `messageId` |
| `UserMessageRecalled` | `_crystal/recalled` notification with the draft (trusted markers kept) |
| `StreamReceived` text | `agent_message_chunk`, `messageId` per model round |
| `StreamReceived` reasoning | `agent_thought_chunk` |
| `StreamReceived` reasoning state | dropped; `_meta` on the next thought names the format only |
| `StreamReceived` tool call | `tool_call` (pending) on first sight, `tool_call_update` with `rawInput` as arguments complete |
| `StreamReceived` usage, `UsageChanged`, `ContextEstimated` | `usage_update` with `used` and `size` (selected context budget). `_meta.crystal` holds cumulative input, output, reasoning, the `~` estimate flag, and `interim` |
| `StreamReceived` completed, `ModelRoundClosed` | closes the message id; `_meta` on the next update |
| `RetryScheduled` | `notice` warning with attempt and delay, plus `_crystal/activity` |
| `ToolCallsIssued` | `tool_call` with title, kind, locations, `rawInput` (if not already sent) |
| `ApprovalReviewing` | `tool_call_update` `_meta.crystal.approval = {stage: primary\|extended, state: reviewing}` |
| `ApprovalPassed` | `tool_call_update` status `in_progress`, `_meta.crystal.approval` with reason, risk, authority, summary, and verdicts |
| `ToolResultsReceived` | `tool_call_update` `completed` or `failed` with content and `rawOutput` |
| `TodosChanged` | `plan` |
| `SlashCommandsChanged` | `available_commands_update` |
| `SessionOptionsChanged` | `config_option_update` with the complete list, and `current_mode_update` when the mode changed |
| `SessionStarted`, `ChromeChanged`, `PreferencesChanged` | nothing of their own: the engine publishes `SessionOptionsChanged` for every value they change |
| `SessionStarted`, `ChromeChanged` | also `_crystal/chrome` with the full plain chrome |
| `PreferencesChanged` | also `_crystal/preferences` |
| `StatusFieldsChanged` | `_crystal/statusFields` |
| `ActivityChanged` | `_crystal/activity` (loading tools, loading plugins, compacting, waiting for model) |
| `LoadingProgressed` | `_crystal/loading` with `kind` (`tool_set`, `plugin`, or `mcp_server`) and `name` |
| `QueueChanged` | `_crystal/queue` |
| `NoteWritten`, `ErrorWritten` | `notice` info or error |
| `HelpRequested`, `StatusReported`, `StatsReported`, `ToolsListed`, `TodosListed` | agent message text from the engine's plain-text report renderers (`HelpText`, `StatusText`, `StatsText`, `ToolListText`, `TodoList.Format`), `_meta.crystal.report` with the kind (`help`, `status`, `stats`, `tools`, `todos`), and `_crystal/report` with the kind and the structured values |
| `SideQuestionSnapshot` | `_crystal/side` notification; never conversation content |
| `HistoryReplayed` | the replay sequence above |
| `ConversationCleared` | `session_info_update` with the new `_meta.crystal.engineSessionId` |
| `TurnFinished` (in the prompt response) | `session_info_update` with the saved `title` and `updatedAt` |
| `PromptHistoryLoaded`, `ImageHistoryInvalidated` | `_crystal/promptHistory`, `_crystal/imagesInvalidated` |
| `ChildSessionChanged`, `ChildEventReceived` | see Children |
| `ChildViewRequested` | `_crystal/childView` |
| `CompactionStarted`, `CompactionFinished` | with `session.compaction`: `compaction_update` `in_progress`, then `compaction_summary_chunk` when a summary is shown, then `compaction_update` `completed`, `failed`, or `cancelled` (`unchanged` is sent as `completed` with `_meta.crystal.status`). Without it, a shown summary is `agent_message_chunk` whose `messageId` is the compaction id; the engine notes still arrive as notices |
| `TurnStarted`, `TurnFinished` | `_meta` on the first and last update; the prompt response |

`_crystal/*` notifications use the `params.sessionId` envelope of
`session/update` and are sent only to a client with
`_meta.crystal.extensions`.

## Tool calls

- `toolCallId` is the model's call id. Child calls appear only under their
  child session id, so ids stay unique per session (see Children).
- `name` is the model-facing tool name.
- `title` is the same one-line summary the terminal shows (moved into the
  engine; see Engine changes).
- `kind`:

| Tools | Kind |
| :--- | :--- |
| `read`, `list`, `tree`, `skill`, `view_image`, `describe_image`, `read_mcp_resource`, `list_mcp_resources` | `read` |
| `glob`, `grep`, `search` | `search` |
| `edit`, `write`, `apply_patch` | `edit`; a patch with only Delete File is `delete`, with only a Move is `move` |
| `bash`, `bash_start`, `bash_status`, `bash_logs`, `bash_stop` | `execute` |
| `todowrite`, `todoread` | `think` |
| `question` | `other` |
| `task` | `other`, `_meta.crystal.child` |
| MCP, plugin, and external tools | `other`, `_meta.crystal.source` |

- `locations` list the absolute paths a call reads or writes (with a line
  where the arguments name one), resolved by the same workspace resolver
  approval uses.
- Content: `edit`, `write`, and `apply_patch` send `diff` entries (`path`,
  `oldText`, `newText`) computed from disk before execution, one per file;
  a Delete is a diff with empty `newText` and `_meta.crystal.delete`. Results
  send the tool text as `content`. Tool images send `image` blocks with the
  bytes from `TryGetImage`. `bash` results keep the full text; long output is
  bounded by the existing tool output limit.
- Status flow: `pending` when issued, `in_progress` after approval passes,
  `completed` or `failed` from the result status. A denied call is `failed`
  with the rejection text the model sees. Screened calls (unknown tool,
  wrong input) are `failed` with the screening text.

## Interactions

### Permission

`IApprovalPrompt.AskAsync` becomes `session/request_permission` with the
tool call (title, kind, locations, diff content, `rawInput`) and options:

| optionId | kind | Engine |
| :--- | :--- | :--- |
| `once` | `allow_once` | `AllowOnce` |
| `session` | `allow_always` | `AllowSession`, name "Allow for this session" |
| `always` | `allow_always` | `AllowPersistent`, name "Always allow" |
| `deny` | `reject_once` | `Deny` |

- `session` and `always` are offered only when
  `ToolClassification.AllowsReusableGrant` is true, as on the server.
- `_meta.crystal` carries risk, authority, the host summary, `requirePrompt`,
  the primary and extended review verdicts, and for a child call without
  subagent sessions `child` (see Children).
- `cancelled`, an unknown option, or a closed connection is Deny.
- Classification, forced prompts, Full, review, and extended review are
  unchanged. Bridge never answers a permission itself.

### Forms

With `elicitation.form`, these become `elicitation/create` with
`sessionId` and, for a tool, `toolCallId`:

| Engine | Form | Result |
| :--- | :--- | :--- |
| `question` tool (`IUserPrompt`) | One property per question, keyed `q1..qN`. Single select: string `enum` with option labels, `description` from header, text, and option descriptions. Multiple: array of that enum. Custom allowed: an extra `qN_custom` string. | `accept` maps to `QuestionResponse` in question order; `decline` and `cancel` dismiss |
| MCP elicitation | The server's schema passed through after the engine's validation | `accept`, `decline`, `cancel` as MCP |
| Trust | One boolean `trust` with the directory in the message | `accept` with `true` trusts and is remembered |
| `/resume` choice | A string enum of session ids with previews in `oneOf` titles | the id, or decline |
| `/child` choice | A string enum of child ids | the id, or decline |

URL mode is never used. Without form support, questions are dismissed (the
model gets the existing dismissed result), MCP elicitation is cancelled,
trust is refused with an error, and choices are declined; `/resume <id>` and
`/child <id>` still work.

## Configuration

`configOptions` come from the engine's option catalog (`SessionOptionsChanged`),
in this order. Each value names the slash command that selects it, and
`session/set_config_option` and `session/set_mode` run that command, so
validation, refusal while busy (`-32603`, `command_refused`), and
`config.json` writes match the terminal exactly. A value or option not offered
is `-32602`. The response carries the complete list of offered options.
`thinking`, `context`, `tier`, and `speed` appear only when the model offers a
choice. A select lists only values with a command plus the current value;
`tier` and `speed` can cycle back to unset but not name it, so unset is shown
only while current. Values grouped by provider use ACP's grouped options.

### Choosing which options are offered

An editor shows every config option as a control, so Bridge offers a short
list by default: `mode`, `model`, `thinking`, `approval`, and `agents`. The
other options in the table below stay out of the editor and remain slash
commands.

The choice applies to everything the client sees: the `session/new`,
`session/load`, `session/resume`, and `session/fork` responses, every
`config_option_update`, and the `session/set_config_option` response. An
option that is not offered is `-32602` for `session/set_config_option`, as an
unknown id is, and its slash command still works (`/tokens on`,
`/compact micro on`, `/approval model ...`). The offered options keep the
catalog order of the table, whatever order a list names them in. The legacy
`modes` field and `session/set_mode` are present only while `mode` is offered.

The choice is made in this order, and the first rule that applies wins:

1. A command-line flag, for this process only:
   - `--config-options <IDS>`: a comma-separated list of ids, for example
     `mode,model,approval`. The word `all` stands for every option.
   - `--all-config-options`: every option in the table.
   - `--default-config-options`: the built-in default, even when
     `config.json` has a custom list.
2. The `bridge` section of `config.json`, only while its switch is on:

   ```json
   { "bridge": { "customConfigOptions": true, "configOptions": ["mode", "model", "approval"] } }
   ```

   `customConfigOptions` defaults to `false`, and while it is off the saved
   `configOptions` list is kept but not used, so a list can be switched off and
   on again. `configOptions` takes ids from the table, or `all` for every
   option now and later. With the switch on and no list, the built-in default
   applies. An empty list, an empty entry, or an unknown id stops startup.
   Nothing is written to `config.json` while the section is at its default.
3. The built-in default.

A process flag does not change `config.json`. Hiding an option does not change
its value: a hidden option keeps whatever the terminal or `config.json` set.

| id | category | Values | Command |
| :--- | :--- | :--- | :--- |
| `mode` | `mode` | Plan, Work, enabled custom modes | `/mode` |
| `model` | `model` | grouped by provider; value `provider/model` | `/model <provider> <model>` |
| `thinking` | `thought_level` | the model's gears plus default and off when allowed | `/thinking` |
| `context` | `model_config` | declared budgets | `/context` |
| `tier` | `model_config` | declared tiers plus unset | `/tier` |
| `speed` | `model_config` | declared speeds plus unset | `/speed` |
| `approval` | `_crystal_approval` | default, edit, review, audit, full | `/approval` |
| `approval_model` | `_crystal_approval` | off plus catalog | `/approval model` |
| `extended_approval` | `_crystal_approval` | off plus catalog | `/approval extended` |
| `prompt_set` | `_crystal_prompt` | default plus sets | `/promptset` |
| `agents` | `_crystal_agents` | switch | `/agents on\|off` |
| `micro_compaction` | `_crystal_compaction` | switch | `/compact micro on\|off` |
| `estimated_tokens` | `_crystal_display` | switch | `/tokens on\|off` |

Options a process flag locks (for example `--prompt-set`) are still listed
with their value while they are offered; setting them returns the engine
refusal. The legacy
`modes` field mirrors `mode`. Everything else (`/rules`, `/access`, `/mcp`,
`/tools`, `/plugins`, `/notify`, `/statusline`, `/verbose`, `/todos`) stays
a slash command.

## Slash commands

`available_commands_update` lists every completable verb from
`SlashCommandsChanged`, including plugin and MCP prompt commands, with the
engine help text. Verbs that take arguments get `input.hint` built from the
first argument keys, and `_meta.crystal.arguments` carries the full
`SlashCompletion` tree. Every
`SessionVerb` is reachable by typing it. `/quit` closes the session as
`session/close` does. `/cd <path>` and `/space` move the session and are
reported through `session_info_update._meta.crystal.workspace`.

## Plans

`TodosChanged` maps to `plan`: `content`, `status` (`pending`,
`in_progress`, `completed`; `cancelled` is sent as `completed` with
`_meta.crystal.status = cancelled`), and `priority` `medium`. Child todos go
to the child session when subagent sessions are enabled, and otherwise into
the parent `task` call's `_meta.crystal.child.todos`; they never replace the
parent plan.

## Children

Subagent sessions are a deviation from ACP (see Deviations). They follow
the subagent sessions RFD and are enabled only by
`clientCapabilities._meta.crystal.subagents: {}`.

With that opt-in: each child is announced by `subagent_update` on the parent
(`sessionId` = child id, `title` = label, `description` = Agent name) before
any of its traffic. `ChildEventReceived` events are sent under the child
session id with the same mapping. `state` follows the child status. No
child capability is advertised: the engine does not accept input for a
child, and parent cancellation stops all children. Child approvals and
forms are sent under the child session id and keep their child label; the
child's call is announced in the child session before its permission.

Without it, which is plain ACP: no `subagent_update` and no child session id
is ever sent. The parent `task` tool call carries the child's status, model
calls, tool calls, and its latest text as `tool_call_update` content. Child
approvals and forms use the parent session id and the parent `task` call's
`toolCallId`, with the child label in the title and
`_meta.crystal.child` (`childSessionId`, `agent`, `label`, and the child's
own `toolCallId` and `name`). `_crystal/children/*` returns the full records
either way.

Approval prompts carry no child id, so Bridge records which child issued
each call from that child's `ToolCallsIssued` and forgets it on the result.
Children cannot use the `question` tool, so child forms are only MCP
elicitation.

## Extensions

All are requests on the active session unless noted. Results are explicit
JSON with the server's field names. An unknown `_crystal/*` method is
`-32601`; an engine refusal is `-32603` with `command_refused`.

| Method | Engine |
| :--- | :--- |
| `_crystal/snapshot` | `SessionState` snapshot (the same fields as `GET /snapshot`, without pending interactions) |
| `_crystal/session/fork` | Fork a saved session (see Sessions) |
| `_crystal/recall` | `TryRecall`; returns `recalled`, and the draft follows as `_crystal/recalled` |
| `_crystal/queue/release` | empty input release |
| `_crystal/mode/cycle`, `_crystal/mode/togglePlan` | `CycleMode`, `TogglePlan`; returns `mode` and `planMode` |
| `_crystal/verbose {target, enabled}` | `SetVerbose` |
| `_crystal/draft {text}` | `NotifyDraftChanged` |
| `_crystal/image/attach {data}`, `_crystal/image/get {number}` | `TryAttachImage`, `TryGetImage` |
| `_crystal/quickPrompts/list`, `_crystal/quickPrompts/send {name}` | the F6 catalog, `SubmitQuickPromptAsync` |
| `_crystal/side/ask {question}`, `_crystal/side/cancel`, `_crystal/side/clear` | `/btw`, `TryCancelSideQuestion`, `ClearSideQuestions` |
| `_crystal/children/list`, `_crystal/children/get {childSessionId}` | child summaries; one record with archive, usage, and todos |
| `_crystal/export {format, system}` | `TryExport` markdown or json; returns `fileName`, `mediaType`, `text` |
| `_crystal/compact {full}` | `/compact` or `/compact full` |
| `_crystal/status {full}`, `_crystal/stats {scope}`, `_crystal/tools` | the command, answered with the structured report values |

A client cannot type the invisible marker prefix, so a prompt names an image
from `_crystal/image/attach` as its plain `[Image #N]`. Bridge turns that
text into the trusted marker once, and only for a number attached that way
and not yet used. Any other `[Image #N]` stays plain text.

Every new public `CodingSession` member must be mapped here or in a standard
method, or listed as an exclusion; the parity tests enforce it.
`PasteClipboardImageAsync` is excluded because it reads this machine's
clipboard; `_crystal/image/attach` replaces it.

## Not supported

- `fs/*` and `terminal/*` client methods (decision above).
- `session/delete`: the engine has no delete semantics for saved sessions.
- `authenticate`, `logout`, and the configurable LLM provider RFD: provider
  definitions and keys stay Home-only, and keys never cross the protocol.
- A client-provided system prompt (RFD): every model-bound string is
  authored here or by the operator's prompt files.
- Audio input, URL-mode elicitation, MCP-over-ACP, and several concurrent
  sessions in one process.

## Security

- No secret on the command line, in a wire field, in a notice, or in stderr.
  Editor MCP server values are treated like provider definitions: never in
  events, `ToString`, logs, or errors.
- Trust, approval modes, grants, forced prompts, the credential-path ban,
  read-only directories, and Full are unchanged. A client that auto-approves
  permissions is the operator's choice, the same as Full.
- Prompt text, resources, tool output, and MCP content are untrusted data for
  the model, as everywhere else.
- Bridge listens on nothing. Its only peer is the process that started it.

## Engine changes

Each is front-end neutral, public, plain data, and usable by
`crystal server` too.

1. Session MCP servers: launch input carries `McpServerDefinition` values
   with `McpServerSource.Session`; discovery merges them after Project; they
   are excluded from session files and from every text that names server
   configuration. This closes the "servers passed in by a front end" MCP gap.
   Done: `SessionFrontEnd.McpServers`, merged by
   `McpServerDiscovery.WithSession`.
2. Tool call presentation (done in phase 1): `ToolCallSummary.Text` holds
   the one-line title the terminal's `ToolCallText.Summary` showed, and
   `ToolFileChanges` returns the affected paths and per-file `ToolFileChange`
   records (old and new text, delete, move) computed from disk. The
   terminal's `ApprovalDiff` stays in the terminal: it builds painted
   line-level hunks for the approval card, which Bridge does not need.
3. Saved sessions: `SessionStore.List` is reachable from a front end with a
   workspace filter, `SessionStore.TrySummarize(id)` reads one summary, and
   resume-by-id uses `SessionLaunchRequest.ResumeTarget` without a chooser
   (done). Fork (done): `SessionLaunchRequest.Fork` with a `ResumeTarget`
   opens a `SessionFork` copy under a new id; `SessionLaunch.Forked` makes
   the new session seed directory rules as a new session does and save at
   once. The source file is not changed.
4. Compaction events (done): `CompactionStarted(Id, Manual)` and
   `CompactionFinished(Id, Status, Summary)`, with ids `compaction-N`,
   status `completed`, `unchanged`, `failed`, or `cancelled`, and the summary
   only when the operator settings show it. `crystal server` sends them as
   `compaction.started` and `compaction.finished`.
5. Option catalogs (done): `SessionOptionsChanged` carries `SessionOption`
   records (id, name, category, current value, switch flag) with
   `SessionOptionValue` entries that each name the slash command selecting
   them. The session publishes it at start and whenever a value or its
   choices change, and not when nothing changed. `crystal server` sends it
   as `options.changed`.
6. Report values (done): `StatusReported`, `StatsReported`, and `ToolsListed`
   keep their plain data, and the plain-text renderers live in the engine:
   `StatusText`, `ToolListText`, `TodoList.Format`, and the new `HelpText`
   and `StatsText`.
7. Attach an image by path (done): `CodingSession.TryAttachImageFile` with
   the same resolution, size, and signature checks as `/attach`, returning
   the trusted marker or the reason it failed instead of writing an error.
   `/attach` and Bridge's `file://` image links both use it, so Bridge never
   reads workspace files itself.
8. Additional roots (done): `SessionLaunchRequest.AdditionalRoots`, read by
   `SessionLaunchAccess.TryReadRoots`, replaces every rule outside the
   workspace when the session is created and keeps read rules on or inside
   it (see Additional roots).

`SessionEventDataTests` covers every new event.

## Tests

`CrystalCode.Bridge.Tests` drives Bridge over an in-memory duplex stream with
a scripted model:

- initialize, new, prompt with text, image, resource, and resource link,
  every stop reason, cancel during streaming, during a tool, and during a
  pending permission;
- prompt content never fails for an advertised block: an image on a
  text-only model and an invalid image become mentions with a notice; a
  binary blob becomes a mention; `resource_link` with a workspace file, a
  directory, a missing path, a `file://` image (attached on an image model,
  mentioned otherwise), a remote-host `file://`, `https`, and an unknown
  scheme each produce the documented mention or marker; nothing is fetched;
  control characters and newlines in names and URIs are removed; a later
  read of a linked outside path still goes through approval;
- permission options with and without reusable grants, every answer, and
  cancellation as deny; review and extended review metadata;
- question, MCP elicitation, trust, resume, and child forms, with and
  without form support;
- load replay order, resume without replay, list paging, close, fork, `/clear`
  keeping the ACP id;
- additional roots: load and resume replace saved outside rules with the
  request list, omitted and `[]` restore nothing, narrowing `read` rules
  inside `cwd` survive, an entry inside `cwd` is `-32602`; load with a
  different `cwd` moves the session and is advertised as a deviation;
- error codes: no response ever carries `-32000`; a missing credential,
  a busy refusal, a failed turn, and context overflow are `-32603` with
  their `kind`; unknown session ids are `-32002`;
- every config option, refusal while busy, and the complete list of offered
  options in the response;
- the offered set: the built-in default, a list, `all`, the config switch
  and its precedence against the three flags, a hidden option refused over
  ACP and still usable as a slash command, and `modes` present only with
  `mode`;
- slash commands, reports, notices, compaction with and without
  `session.compaction`, plans, usage, queue, follow-up prompts;
- children with and without `_meta.crystal.subagents`: without it no
  `subagent_update` and no child session id appears on the wire, and child
  approvals use the parent session id and `task` call; with it child
  approvals use the child session id; a root `clientCapabilities.subagents`
  alone does not enable it; `initialize` never sends
  `sessionCapabilities.fork` or any other non-schema field outside `_meta`;
- `_crystal/*` methods, and no `_crystal/*` notification without
  `_meta.crystal.extensions`;
- stdout carries only protocol lines even when a plugin writes to the
  console;
- parity: every public `CodingSession` member, `SessionFrontEnd` contract,
  `SessionEvent` subclass, Crystal `ChatStreamEvent`, and `SessionVerb` is
  mapped or excluded with a reason;
- secret leak: a literal provider key and editor MCP env and header values
  never appear on stdout or stderr after `/status full`, `/tools`, `/mcp`,
  `/model`, and errors;
- dependency guard: no Display, Server, Spectre.Console, Terminal.Gui, the
  executable, or `System.Console`.

- debug log: both directions are logged with `--debug`, nothing without it,
  editor MCP values never appear, sensitive fields are replaced at any depth,
  long strings are cut, and invalid JSON is not echoed.

`CrystalCode.Tests` covers `crystal bridge --help`, console rebinding, and the
`--debug` sink (none, stderr, appended file, unwritable path).
Manual checks: the ACP Test Compatibility Kit and Zed.

## Phases

1. Transport, initialize, new, prompt, cancel, close,
   `/clear` id handling, message and thought chunks, tool call lifecycle
   with titles, kinds, locations, and diffs, permission, plans, stop
   reasons, stdout discipline, parity skeleton.
2. Load with replay, resume, list, config
   options and modes, available commands, plans, usage, notices, reports.
3. Session MCP servers, additional directories, `file://` image links,
   forms for question, MCP elicitation, trust, and choices.
4. Children and subagents, compaction updates, fork, every `_crystal/*`
   method and notification.

All four are in this build.

### Scope in this build

- Every prompt content block is in, as Prompt content and Links describe,
  including `file://` image links. Bridge removes U+2063 from client text
  and resources, so only an image it attached has a trusted marker.
- `mcpServers` are session servers. A stdio command must be an absolute path or a bare
  command name resolved through PATH (Xcode sends `xcrun`). A bad
  entry is `-32602` naming the server and field, never a value.
- Forms need `elicitation.form`: the `question` tool and MCP elicitation
  share the question form (MCP elicitation reaches a front end as
  questions, so its schema is the engine's question shape, not the server's
  raw schema). Trust before a session is a form tied to the `session/new`,
  `load`, or `resume` request id; inside a session it is session-scoped.
  `/resume` and `/child` choices are forms. Without forms the fallbacks
  under Forms apply.
- `session/load` and `session/resume` keep the requested id as the ACP id.
- Notes and retries are notices only for a client with `session.notices`;
  an error without notices is agent message text.
- Every `SessionEvent` is in `SessionUpdates.Mapped` (a `session/update`)
  or `SessionUpdates.Notified` (a `_crystal/*` notification). Only
  `UserMessageSent`, `TurnStarted`, and `TurnFinished` are in
  `SessionUpdates.Unmapped`, with their reason; the parity test keeps the
  lists complete and disjoint.
- Fork answers with the new `sessionId`, and that id is the ACP id.
