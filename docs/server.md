# crystal server

`crystal server` is a second operator surface beside the terminal. An operator,
or a wrapper program such as a desktop application, starts it in a workspace.
Separate front ends, for example a web page or a desktop program, then connect
to it over HTTP and drive the session. Those front ends are not part of this
repository.

Status: the behavior below is defined. The server is not in this build.

Engine boundary already in this build:

- `CrystalCode.Engine.Launch` (`SessionLauncher`, `SessionLaunchOptions`,
  `SessionLaunchOverrides`, `SessionLaunchAccess`): the launch path the
  terminal and `crystal run` use.
- `SessionRunner`: the input loop `crystal run` uses.
- Plain-data report events: `ToolsListed` carries a `ToolCatalogReport` and
  `HelpRequested` carries `SlashCompletion` values.
- `CodingSession.TryAttachImage`, `TryGetImage`, `TryExport`, and the
  structured `SessionClosure` from `CloseAsync`.

Engine boundary still to build with the server:

- A front-end-neutral session state reducer that produces the snapshot. The
  terminal's child projection moves onto it.
- An origin on events a request produced, so replies reach one client.
- Draft images and recalled messages tracked per origin, so one client's
  draft edit cannot discard another client's attachment.

## Shape

- One process hosts one active `CodingSession`. `/clear`, `/resume`, `/fork`,
  `/cd`, and `/space` replace or move that session inside the process, exactly
  as in the terminal. A front end that wants several sessions starts several
  processes.
- The server is a front end that implements `SessionFrontEnd`. It holds no
  product logic. Approval, trust, the workspace fence, and
  `ToolInvocationPolicy` stay where they are. The server only carries their
  questions to a connected client.
- Every engine capability is reachable: every slash command, every
  `CodingSession` operation a front end calls, every `SessionFrontEnd`
  contract, and every `SessionEvent`.
- The protocol carries no version identifier in routes, headers, or fields.
  It is not a public web API. It follows the build: a wrapper knows which
  executable it started, and `GET /info` reports the build identity.

## Projects

| Project | Owns |
| :--- | :--- |
| `CrystalCode.Server` | ASP.NET Core Minimal API endpoints and server-sent events on Kestrel, authentication, the wire DTOs (System.Text.Json source generation), the interaction broker, the event log, and the session host that wires `SessionRunner` to HTTP. References `CrystalCode.Engine` and the `Microsoft.AspNetCore.App` shared framework. It does not reference `CrystalCode.Display`, Spectre.Console, Terminal.Gui, or the executable. |
| `CrystalCode.Server.Tests` | Tests against a real Kestrel listener on port 0 or a temporary Unix socket, driven by a scripted model. |
| `CrystalCode` | `Commands/ServerCommand` parses the command line and hands off to `CrystalCode.Server`. |

Wire DTOs are owned by the server. Engine records, Crystal chat types, and
plugin interfaces are never serialized directly.

## Command line

```text
crystal server [session launch options] [-w|--workspace <PATH>] [-r|--resume [TARGET]]
               [--listen <HOST:PORT>] [--socket <PATH>]
               [--tls-cert <PATH> --tls-key <PATH>]
               [--auth token|none] [--token-file <PATH>] [--token-stdin]
               [--allow-host <HOST>]... [--allow-origin <ORIGIN>]...
               [--exit-on-stdin-close]
```

- Session launch options are the same set `crystal` accepts. Like every
  launch flag they shape this process only and are not written to
  `config.json` or `prompt.json`.
- `--listen` defaults to `127.0.0.1:0`. Port `0` picks a free port.
- `--socket` listens on a Unix domain socket with mode `0600` instead of TCP.
- A TLS private key password comes from `CRYSTAL_SERVER_TLS_PASSWORD`, never
  from the command line.
- No secret is a command flag.

## Startup

The server moves through `starting`, `awaiting_operator`, `ready`, and
`closing`. It reports the state in `GET /health`, `GET /info`, and the
`server.state` event.

- Launch uses `SessionLauncher`, the same path as the terminal. When the
  workspace needs trust (the `workspaceTrust` switch applies) or `--resume`
  needs a choice, the state is `awaiting_operator` and the question is an
  interaction answered by the first client with the `operate` scope. When
  stdin is a TTY the operator may answer in the terminal instead.
- Once listening, the server writes one JSON line to stdout for a wrapper:

  ```json
  {"event":"listening","url":"http://127.0.0.1:52144","pid":4242,"workspace":"/repo","discovery":"/home/me/.crystal/server/4242.json"}
  ```

  The line never carries a token.
- It writes the discovery file `~/.crystal/server/<pid>.json` with owner-only
  permissions: url, pid, workspace, session id, build identity, and the token
  when the server generated it. The file is deleted on exit.

## Authentication

- `--auth token` is the default on every listener, including loopback and a
  Unix socket, because other local processes and browser pages can reach
  loopback.
- Token sources, first match wins: `CRYSTAL_SERVER_TOKEN`, `--token-file`
  (an owner-only file), `--token-stdin`, otherwise a generated 256-bit random
  token written to the discovery file and printed only when stdout is a TTY.
  Tokens never appear in arguments or logs. Comparison is constant time.
- `--auth none` turns token checks off. Every request then has every scope.
  The server prints a warning at start. Host and Origin checks still apply.
- A non-loopback listener without TLS is allowed. The server prints a warning
  that tokens and session content travel in clear text.
- Requests send `Authorization: Bearer <token>`. `GET /health` needs no token
  and returns only the state.
- Scopes:
  - `observe`: snapshot, events, images, export, children, quick prompt list.
  - `operate`: input, interrupt, recall, mode, verbose, draft, image upload,
    quick prompt send, side question control, interaction answers.
  - `admin`: shutdown, token management, and slash commands that change
    trust, directory access, approval mode, the workspace, or persisted
    configuration. Each `SessionVerb` is assigned a scope, and a test covers
    the whole enum.
- The startup token has every scope. `POST /auth/tokens` issues a narrower
  token (for example observe only) that lives for this process and can be
  revoked with `DELETE /auth/tokens/{id}`. Issued tokens are not persisted.
- Browsers cannot set headers on `EventSource`. `POST /auth/ticket` returns a
  single-use ticket valid for 30 seconds that `GET /events?ticket=` accepts.
  A long-lived token never goes in a URL.
- The `Host` header must name loopback, the listen address, or an
  `--allow-host` value, which blocks DNS rebinding. CORS is off unless
  `--allow-origin` lists an origin. A request with an `Origin` header that is
  not listed is rejected.
- Failed authentication is rate limited. Request bodies have a size limit;
  image uploads keep the 20 MiB host limit.

## Conventions

- JSON with camelCase fields over UTF-8. Server-sent events for the stream.
- Every request carries `X-Crystal-Client: <random id>` chosen by the client
  for that page or window. It routes targeted events and records which client
  started a request.
- Every state-changing `POST` carries `clientRequestId`. A repeat within ten
  minutes returns the first result without running again.
- Errors are `application/problem+json` with English text.
- Clients ignore event types and fields they do not know.

## Endpoints

| Route | Scope | Purpose |
| :--- | :--- | :--- |
| `GET /health` | none | State only |
| `GET /info` | observe | Build identity, capability lists (event types, operations, slash commands, interaction kinds), session id, workspace |
| `GET /snapshot` | observe | Session state, `lastSeq`, pending interactions, server state |
| `GET /events?after=<seq>` | observe | Event stream; honors `Last-Event-ID` |
| `POST /input` | operate | Any text: a prompt, a slash command, a follow-up, or empty text that releases the queue |
| `POST /interrupt` | operate | `TryInterrupt` |
| `POST /recall` | operate | `TryRecall`; the client states that its composer can take the message back |
| `POST /mode` | operate | `cycle`, `toggle-plan`, or `set` with a name |
| `POST /verbose` | operate | `SetVerbose` (Ctrl+O and Ctrl+G) |
| `PUT /draft` | operate | `NotifyDraftChanged` |
| `POST /images` | operate | `TryAttachImage`; returns the number and trusted marker |
| `GET /images/{number}` | observe | `TryGetImage`; bytes of a session or tool image |
| `GET /quick-prompts` | observe | The F6 catalog |
| `POST /quick-prompts/{name}/send` | operate | `SubmitQuickPromptAsync`; Fill stays in the client |
| `POST /side/cancel`, `POST /side/clear` | operate | Side question control; `/btw` itself goes through `/input` |
| `GET /children`, `GET /children/{id}` | observe | Child list, archive, events, usage, and todos (F7 and `/child`) |
| `GET /export?format=markdown\|json&system=true\|false` | observe | `TryExport` as a download; `/export` still writes on the server |
| `GET /interactions`, `POST /interactions/{id}` | operate | Pending interactions and answers |
| `POST /auth/ticket` | observe | Event-stream ticket |
| `POST /auth/tokens`, `DELETE /auth/tokens/{id}` | admin | Narrower tokens |
| `POST /shutdown` | admin | Same as `/quit` |

## Events

Each event is one SSE message whose `id` is `seq`:

```json
{"seq":1042,"type":"stream.text","time":"2026-10-26T02:36:20Z","sessionId":"…",
 "source":{"childSessionId":"…","invocationId":"…"},"target":"client-7f3a",
 "data":{"delta":"…"}}
```

`source` appears only on child events (`ChildEventReceived` is unwrapped).
`target` appears only on events meant for one client. Every `SessionEvent` and
every Crystal `ChatStreamEvent` maps to one type:

| Engine event | Type |
| :--- | :--- |
| `SessionStarted`, `ChromeChanged` | `session.started`, `chrome.changed` |
| `PreferencesChanged`, `StatusFieldsChanged` | `preferences.changed`, `status-fields.changed` |
| `SlashCommandsChanged`, `PromptHistoryLoaded` | `commands.changed`, `prompt-history.loaded` |
| `ImageHistoryInvalidated`, `ActivityChanged` | `images.invalidated`, `activity.changed` |
| `UserMessageSent`, `UserMessageRecalled` | `message.sent`, `message.recalled` |
| `TurnStarted`, `TurnFinished`, `ModelRoundClosed` | `turn.started`, `turn.finished`, `round.closed` |
| `StreamReceived` | `stream.text`, `stream.reasoning`, `stream.reasoning-state`, `stream.tool-call`, `stream.usage`, `stream.completed` |
| `ToolCallsIssued`, `ToolResultsReceived` | `tools.called`, `tools.results` |
| `RetryScheduled`, `UsageChanged`, `ContextEstimated` | `retry.scheduled`, `usage.changed`, `context.estimated` |
| `QueueChanged`, `TodosChanged` | `queue.changed`, `todos.changed` |
| `HistoryReplayed`, `ConversationCleared` | `conversation.replayed`, `conversation.cleared` |
| `NoteWritten`, `ErrorWritten` | `note`, `error` |
| `HelpRequested`, `StatusReported`, `StatsReported`, `ToolsListed`, `TodosListed` | `report.help`, `report.status`, `report.stats`, `report.tools`, `report.todos` |
| `SideQuestionSnapshot` | `side.changed` |
| `ApprovalPassed`, `ApprovalReviewing` | `approval.passed`, `approval.reviewing` (with stage `primary` or `extended`) |
| `ChildSessionChanged`, `ChildViewRequested` | `child.changed`, `child.view` |

Server events: `server.state`, `server.closing` (with the session id and
whether it was saved), `interaction.opened`, `interaction.resolved`,
`interaction.cancelled`, and `resync`.

`stream.reasoning-state` reports that opaque provider state arrived. It does
not carry the state. Image bytes never appear in events; markers and numbers
do.

Delivery: state events go to every client. Reports, `child.view`, and the
notes and errors a request produced go to the client that sent that request.
The engine marks those events with their origin.

## Snapshot

`GET /snapshot` returns the state a client needs to draw the session without
replaying history: chrome, preferences, slash commands, prompt history,
archive messages, the in-progress model round (text, reasoning, tool-call
arguments so far), queue, todos, status fields, activity, usage, child
summaries, the side-question thread, pending interactions, and `lastSeq`. The
snapshot and `lastSeq` are taken under one lock, so a client that follows
with `GET /events?after=lastSeq` misses nothing and sees nothing twice. The
snapshot is built by the engine's front-end-neutral session state, not by
server code.

## Interactions

Anything that asks the operator becomes an interaction with an `id`, a
`kind`, and a payload.

| Kind | Engine contract | Answer |
| :--- | :--- | :--- |
| `approval` | `IApprovalPrompt.AskAsync` | `once`, `session`, `always`, or `deny`; calls that cannot reuse a grant offer only `once` and `deny` |
| `question` | `IUserPrompt.AskAsync` | Selected labels and custom text per question, or dismissed |
| `trust` | `IWorkspaceTrustPrompt.ConfirmAsync` | Yes or no |
| `choose_session` | `ISessionChooser.ChooseAsync` | A session id or declined |
| `choose_child` | `SessionFrontEnd.ChooseChildAsync` | A child id or declined |

- An approval payload carries the call, the classification, the primary
  review verdict and the extended review verdict when they ran, the choices
  allowed, and the child that raised it.
- Interactions raised by the model (approval, question) go to every client
  with `operate`. The first valid answer wins and the rest receive
  `interaction.resolved`.
- Interactions raised by a client command (`choose_session` from `/resume`,
  `choose_child` from `/child`, `trust` from `/cd`) go to that client. If it
  disconnects, they go to every client with `operate`.
- A cancelled turn cancels its interactions. Otherwise an interaction waits,
  as the terminal card does.

## Refresh and reconnect

- Events also go into a ring buffer bounded by count and bytes.
- Refresh: `GET /snapshot`, then `GET /events?after=lastSeq`.
- Reconnect: the browser sends `Last-Event-ID`. When that cursor has left the
  buffer the server sends `resync` and closes the stream; the client fetches a
  new snapshot.
- The stream sends a comment heartbeat every 15 seconds and `retry: 2000`. A
  slow client falls behind on its own cursor and never blocks the engine.
- `/clear`, `/resume`, and `/fork` change the session id. The change arrives
  in the stream like any other event.

## Shutdown

`/quit`, `POST /shutdown`, SIGINT, SIGTERM, or stdin closing with
`--exit-on-stdin-close` start shutdown: interrupt the turn, `FinishTurnAsync`,
`CloseAsync`, publish `server.closing`, delete the discovery file, exit.

## Parity guards

`CrystalCode.Server.Tests` fails the build when:

- a public `CodingSession` member or a `SessionFrontEnd` contract has no
  endpoint or interaction and no recorded exclusion;
- a `SessionEvent` subclass or a Crystal `ChatStreamEvent` has no wire type;
- a `SessionVerb` has no scope;
- a provider with a literal API key leaks that key into the snapshot or the
  stream after `/tools`, `/status full`, `/stats`, and `/help`.

## Not in scope

- Several active sessions in one server process, and several concurrent
  tasks in one terminal process. Both are deferred in `TODO.md`. Plugin and
  external tool load contexts are not collectible, so a long-lived process
  that loaded them per session would only grow.
- MCP servers supplied by a front end for one session (see MCP gaps in
  `TODO.md`).
- The front ends themselves.
