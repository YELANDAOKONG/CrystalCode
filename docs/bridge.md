# crystal bridge

Status: phase 1 is in this build (see Phases and Phase 1 scope).
`crystal bridge`, `CrystalCode.Bridge`, and `CrystalCode.Bridge.Tests` exist.
Phases 2 to 4 are high-priority product work and are not in this build; the
rest of this document defines the behavior and ownership they must follow.
The item leaves High priority in TODO.md when phase 4 lands.

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

## Projects and ownership

| Project | Owns |
| :--- | :--- |
| `CrystalCode.Bridge` | `Transport` (newline-delimited JSON-RPC 2.0 over streams, request ids, `$/cancel_request`, error codes), `Wire` (every field written explicitly with `Utf8JsonWriter`: `SessionUpdates` maps `SessionEvent` and Crystal stream events to `session/update`, `ToolCallWire`, `PermissionWire`, `TurnWire`), `Prompts` (content blocks to submitted text), and `Hosting` (`CrystalBridge` entry, `BridgeAgent` methods, `BridgeSession` with the live `CodingSession`, `SessionRunner`, front-end contracts, and turn tracking). Later phases add `Interactions` (elicitation), `Configuration` (config options and legacy modes), and `Extensions` (`_crystal/*` methods). References `CrystalCode.Engine` only. |
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
```

- Session launch options are the set `crystal` accepts. They shape this
  process only and are not written to `config.json` or `prompt.json`. The
  launch provider and model may be saved by a later `/model`, as on the
  terminal.
- `--workspace` and `--space` only set the fallback for a session request
  whose `cwd` is missing. ACP always sends `cwd`, so in practice the client
  chooses the workspace.
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

`initialize` returns, once every phase is in:

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

Phase 1 sends only what it implements: `loadSession: false`,
`mcpCapabilities` `http` and `sse` false, `sessionCapabilities` with `close`
only, and `_meta.crystal.extensions: []`. `agentInfo.version` is the first
12 characters of the product commit, or `dev` for a build without one.
`protocolVersion` is always 1, whatever the client sent.

- `agentInfo.version` is the short product commit from the build identity.
- `image` and `embeddedContext` are always true. Bridge accepts every block
  it advertises and never fails a prompt because the current model cannot
  use one; see Prompt content. `audio` is false because the engine has no
  audio input, so clients do not send it.
- Fork and subagents are RFDs, not stable ACP, so they appear only under
  `_meta.crystal` (see Deviations). When either reaches the stable schema,
  the change that adopts it moves it to the spec field and updates this
  document.
- `_meta.crystal.extensions` lists every `_crystal/*` method this build
  answers. It follows the build and carries no version.
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
| `fs`, `terminal`, `auth.terminal` | Read and ignored. |

## Sessions

| ACP | Engine |
| :--- | :--- |
| `session/new {cwd, mcpServers, additionalDirectories}` | `SessionLauncher.PrepareAsync` with `Workspace = cwd`, then `CreateSession` and `StartAsync`. Returns `sessionId`, `configOptions`, and `modes`. |
| `session/load {sessionId, cwd, ...}` | Prepare with `Resume = true`, `ResumeTarget = sessionId`. Replays the archive (see Replay), then returns `configOptions` and `modes`. |
| `session/resume {sessionId, cwd, ...}` | Same restore without replay. |
| `_crystal/session/fork {sessionId, cwd, ...}`, and RFD `session/fork` (not ACP) | `/fork <id>` semantics: a new id from that saved session, in `cwd`. The source file is not changed. |
| `session/list {cwd, cursor}` | `SessionStore.List(workspaceRoot)`, newest first. `title` is the summary preview, `updatedAt` the update time, `additionalDirectories` the saved outside-workspace roots, `_meta.crystal` carries `mode`, `planMode`, `userTurns`, and `createdAt`. Pages of 50 with an opaque cursor; an invalid cursor is `-32602`. Omitting `cwd` lists every workspace. |
| `session/close {sessionId}` | Interrupt, collect, `CloseAsync`. The process stays up for another `session/new`. |
| `session/set_mode` | `TrySetMode`. |
| `session/set_config_option` | See Configuration. |
| `session/cancel` | `TryInterrupt`; pending permission and elicitation requests for that turn are cancelled and treated as deny. |

- `sessionId` is the engine session id. `/clear` inside a session makes a
  new engine id. Bridge keeps the ACP id stable for the connection and
  reports the new engine id in `session_info_update._meta.crystal.engineSessionId`.
  `session/load` accepts either id.
- `cwd` must be absolute. A `cwd` on load or resume that differs from the
  saved workspace is a deviation from ACP (see Deviations): the session
  enters `cwd`, as `/resume <path>` does after trust.
- Trust uses `SessionTrustCheck.Prompt` and the `workspaceTrust` switch. The
  question goes to the client as a form (see Interactions). A client without
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
`tool_call_update` (final status and content) for each call and result, a
completed `compaction_update` with its summary for each archived
`## Earlier context` block, and a final `plan`, `usage_update`,
`available_commands_update`, and `config_option_update`. Opaque reasoning
state is never sent. Notices are not replayed.

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
| `ChromeChanged`, `PreferencesChanged`, `StatusFieldsChanged` | `config_option_update` and `current_mode_update` when values changed; `_crystal/chrome` with the full plain values |
| `ActivityChanged` | `_crystal/activity` (loading tools, loading plugins, compacting, waiting for model) |
| `QueueChanged` | `_crystal/queue` |
| `NoteWritten`, `ErrorWritten` | `notice` info or error |
| `HelpRequested`, `StatusReported`, `StatsReported`, `ToolsListed`, `TodosListed` | agent message text from the engine's plain-text report renderers, `_meta.crystal.report` with the kind and structured values |
| `SideQuestionSnapshot` | `_crystal/side` notification; never conversation content |
| `HistoryReplayed`, `ConversationCleared` | the replay sequence above, or `_crystal/cleared` |
| `SessionStarted` | `session_info_update` |
| `PromptHistoryLoaded`, `ImageHistoryInvalidated` | `_crystal/promptHistory`, `_crystal/imagesInvalidated` |
| `ChildSessionChanged`, `ChildEventReceived` | see Children |
| `ChildViewRequested` | `_crystal/childView` to the requester |
| compaction start, summary, end | `compaction_update` `in_progress`, `compaction_summary_chunk` or `summary`, `completed` or `failed` |
| `TurnStarted`, `TurnFinished` | `_meta` on the first and last update; the prompt response |

`_crystal/*` notifications use the `params.sessionId` envelope of
`session/update` and are ignored by clients that do not know them.

## Tool calls

- `toolCallId` is the model's call id. Child calls are prefixed with the
  child id so ids stay unique on the connection.
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
  the primary and extended review verdicts, and the child label.
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

`configOptions`, in this order. Every change runs through the matching slash
command so validation, refusal while busy, and `config.json` writes match
the terminal exactly. The response carries the complete list.

| id | category | Values | Command |
| :--- | :--- | :--- | :--- |
| `mode` | `mode` | Plan, Work, enabled custom modes | `TrySetMode` |
| `model` | `model` | grouped by provider; value `provider/model` | `/model <provider> <model>` |
| `thinking` | `thought_level` | the model's gears plus default and off when allowed | `/thinking` |
| `context` | `model_config` | declared budgets | `/context` |
| `tier` | `model_config` | declared tiers plus unset | `/tier` |
| `speed` | `model_config` | declared speeds plus unset | `/speed` |
| `approval` | `_crystal_approval` | plan, default, edit, review, audit, full | `/approval` |
| `approval_model` | `_crystal_approval` | off plus catalog | `/approval model` |
| `extended_approval` | `_crystal_approval` | off plus catalog | `/approval extended` |
| `prompt_set` | `_crystal_prompt` | default plus sets | `/promptset` |
| `agents` | `_crystal_agents` | boolean | `/agents on\|off` |
| `micro_compaction` | `_crystal_compaction` | boolean | `/compact micro` |
| `estimated_tokens` | `_crystal_display` | boolean | `/tokens` |

Options a process flag locks (for example `--prompt-set`) are still listed
with their value; setting them returns the engine refusal. The legacy
`modes` field mirrors `mode`. Everything else (`/rules`, `/access`, `/mcp`,
`/tools`, `/plugins`, `/notify`, `/statusline`, `/verbose`, `/todos`) stays
a slash command.

## Slash commands

`available_commands_update` lists every completable verb from
`SlashCommandsChanged`, including plugin and MCP prompt commands, with the
engine help text. Verbs that take arguments get `input.hint` built from the
first argument keys. `_meta.crystal.arguments` carries the full
`SlashCompletion` tree for a client that wants nested completion. Every
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
forms are sent under the child session id and keep their child label.

Without it, which is plain ACP: no `subagent_update` and no child session id
is ever sent. The parent `task` tool call carries the child's status, model
calls, tool calls, and its latest text as `tool_call_update` content. Child
approvals and forms use the parent session id and the parent `task` call's
`toolCallId`, with the child label in the title and
`_meta.crystal.child`. `_crystal/children` returns the full records either
way.

## Extensions

All are requests on the active session unless noted. Results are explicit
JSON.

| Method | Engine |
| :--- | :--- |
| `_crystal/snapshot` | `SessionState` snapshot (the same fields as `GET /snapshot`) |
| `_crystal/session/fork` | Fork a saved session (see Sessions) |
| `_crystal/recall` | `TryRecall`; returns the draft or `recalled: false` |
| `_crystal/queue/release` | empty input release |
| `_crystal/mode/cycle`, `_crystal/mode/togglePlan` | `CycleMode`, `TogglePlan` |
| `_crystal/verbose` | `SetVerbose` |
| `_crystal/draft` | `NotifyDraftChanged` |
| `_crystal/image/attach`, `_crystal/image/get` | `TryAttachImage`, `TryGetImage` |
| `_crystal/quickPrompts/list`, `_crystal/quickPrompts/send` | the F6 catalog, `SubmitQuickPromptAsync` |
| `_crystal/side/ask`, `_crystal/side/cancel`, `_crystal/side/clear` | `/btw`, `TryCancelSideQuestion`, `ClearSideQuestions` |
| `_crystal/children/list`, `_crystal/children/get` | child summaries, archive, usage, todos |
| `_crystal/export` | `TryExport` markdown or json, with `system` |
| `_crystal/compact` | `/compact` or `/compact full` |
| `_crystal/status`, `_crystal/stats`, `_crystal/tools` | structured report values |

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
2. Tool call presentation (done in phase 1): `ToolCallSummary.Text` holds
   the one-line title the terminal's `ToolCallText.Summary` showed, and
   `ToolFileChanges` returns the affected paths and per-file `ToolFileChange`
   records (old and new text, delete, move) computed from disk. The
   terminal's `ApprovalDiff` stays in the terminal: it builds painted
   line-level hunks for the approval card, which Bridge does not need.
3. Saved sessions: `SessionStore.List` is reachable from a front end with a
   workspace filter, plus `TryForkSaved(id)` and resume-by-id without a
   chooser.
4. Compaction events: a `CompactionStarted` and `CompactionFinished` pair
   with id, manual or automatic, status, and the displayable summary when the
   operator settings show it. Today the summary reaches front ends only as a
   note (`CodingSession.cs:5685` writes `Compacted context`).
5. Option catalogs: a plain report of selectable models by provider, gears,
   context budgets, tiers, speeds, approval modes, modes, and prompt sets,
   published when they change, so config options do not parse help text.
6. Report values: `StatusReported`, `StatsReported`, and `ToolsListed` keep
   their plain data; the plain-text renderers Bridge needs live in the
   engine (`Sessions` already owns status, usage, and tool-list text).
7. Attach an image by path: a public `CodingSession` member with the same
   resolution, size, and signature checks as `/attach`
   (`CodingSession.AttachImage`, which uses
   `_workspace.TryResolveExistingFile`), returning the trusted marker or the
   reason it failed instead of writing an error. `/attach` and Bridge's
   `file://` image links both use it, so Bridge never reads workspace files
   itself.

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
- every config option, refusal while busy, and the complete list in the
  response;
- slash commands, reports, notices, compaction, plans, usage, queue,
  follow-up prompts;
- children with and without `_meta.crystal.subagents`: without it no
  `subagent_update` and no child session id appears on the wire, and child
  approvals use the parent session id; a root `clientCapabilities.subagents`
  alone does not enable it; `initialize` never sends
  `sessionCapabilities.fork` or any other non-schema field outside `_meta`;
- every `_crystal/*` method;
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

`CrystalCode.Tests` covers `crystal bridge --help` and console rebinding.
Manual checks: the ACP Test Compatibility Kit and Zed.

## Phases

1. In this build. Transport, initialize, new, prompt, cancel, close,
   `/clear` id handling, message and thought chunks, tool call lifecycle
   with titles, kinds, locations, and diffs, permission, plans, stop
   reasons, stdout discipline, parity skeleton.
2. Load with replay, resume, list, config
   options and modes, available commands, plans, usage, notices, reports.
3. Session MCP servers, additional directories, `file://` image links,
   forms for question, MCP elicitation, trust, and choices.
4. Children and subagents, compaction updates, fork, every `_crystal/*`
   method, and the documentation move out of High priority.

### Phase 1 scope

- Every prompt content block is in: text, image, resource (text and blob),
  and `resource_link` as described under Prompt content and Links, except
  that a `file://` image link is mentioned, not attached (engine change 7).
  Bridge removes U+2063 from client text and resources, so only an image it
  attached has a trusted marker.
- `mcpServers` is accepted and ignored. Stderr notes how many were
  ignored and never their names, commands, or values.
- A non-empty `additionalDirectories` is `-32602`.
- Trust has no form yet, so an untrusted `cwd` is always `trust_refused`.
  The `question` tool is dismissed and session and child choices are
  declined, as the no-forms fallback describes.
- Notes and retries are notices only for a client with `session.notices`;
  an error without notices is agent message text.
- `TodosChanged` is sent as `plan`. Usage, chrome, queue, reports, and the
  other events wait for their phase; `SessionUpdates.Unmapped` lists each
  with its phase and the parity test keeps that list complete.
