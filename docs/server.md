# crystal server

`crystal server` is a second operator surface beside the terminal. An operator,
or a wrapper program such as a desktop application, starts it in a workspace.
Separate front ends, for example a web page or a desktop program, then connect
to it over HTTP and drive the session. Those front ends are not part of this
repository.

The server is in this build. It uses these engine pieces, which any other
front end can use too:

- `CrystalCode.Engine.Launch` (`SessionLauncher`, `SessionLaunchOptions`,
  `SessionLaunchOverrides`, `SessionLaunchAccess`): the launch path the
  terminal and `crystal run` also use.
- `SessionRunner`: the serialized input loop. `crystal run` uses it too.
- `SessionState`: the front-end-neutral reducer that produces
  `SessionSnapshot`. `ChildSessionFold` is shared with the terminal's child
  projection.
- `CodingSession.BeginOrigin` and `SessionEvent.Origin`: events a client's
  request produced carry that client's id. Draft images, pending `/attach`
  images, and recalled messages belong to the origin that made them.
- Plain-data report events: `ToolsListed` carries a `ToolCatalogReport` and
  `HelpRequested` carries `SlashCompletion` values.
- `CodingSession.TryAttachImage`, `TryGetImage`, `TryExport`, and the
  structured `SessionClosure` from `CloseAsync`.

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
- Front ends probe `GET /info` instead of a version. These fields keep their
  names: `product`, `build`, `state`, `scope`, `authentication`,
  `operations`, `eventTypes`, `interactionKinds`, and `slashCommands`. New
  fields and new list entries may appear; a name is not changed or removed.
  A test fixes the list.

## Projects

| Project | Owns |
| :--- | :--- |
| `CrystalCode.Server` | ASP.NET Core Minimal API endpoints and server-sent events on Kestrel (`Endpoints`), authentication, scopes, and Host and Origin checks (`Security`), the wire mapping (`Wire`), the event log (`Events`), the interaction broker (`Interactions`), and the host that wires `SessionRunner` to HTTP (`Hosting`). References `CrystalCode.Engine` and the `Microsoft.AspNetCore.App` shared framework. It does not reference `CrystalCode.Display`, Spectre.Console, Terminal.Gui, the executable, or `System.Console`. |
| `CrystalCode.Server.Tests` | Tests against a real Kestrel listener on a loopback port chosen by the OS, driven by a scripted model. |
| `CrystalCode` | `Commands/ServerCommand`, `ServerSettings`, `ServerTokenSource`, and `ShutdownSignals` parse the command line, resolve the token, map signals, and hand off to `CrystalCode.Server`. |

The server owns the wire shape. `WireWriter` and `WireEvents` write every
field explicitly with `Utf8JsonWriter`. Engine records, Crystal chat types,
and plugin interfaces are never serialized by reflection, so settings,
provider definitions, opaque reasoning state, and image bytes cannot reach a
client by accident.

## Command line

```text
crystal server [session launch options] [-w|--workspace <PATH>] [-r|--resume [TARGET]]
               [--listen <HOST:PORT>] [--socket <PATH>]
               [--tls-cert <PATH> --tls-key <PATH>]
               [--auth token|none] [--token-file <PATH>] [--token-stdin]
               [--allow-host <HOST>]... [--allow-origin <ORIGIN>]...
               [--expose-header <NAME>]... [--allow-private-network]
               [--exit-on-stdin-close]
```

- Session launch options are the same set `crystal` accepts. Like every
  launch flag they shape this process only and are not written to
  `config.json` or `prompt.json`.
- `--listen` defaults to `127.0.0.1:0`. Port `0` picks a free port. The host
  is an IP address or `localhost`; `0.0.0.0` and `[::]` listen on every
  interface.
- `--socket` listens on a Unix domain socket with mode `0600` instead of TCP.
  It cannot be combined with TLS.
- `--space` uses the operator space, as on `crystal run`, and cannot be
  combined with `--workspace`.
- `--exit-on-stdin-close` ends the server when the wrapper closes stdin. It
  is ignored with `--token-stdin`, which reads the token from stdin.
- A TLS private key password comes from `CRYSTAL_SERVER_TLS_PASSWORD`, never
  from the command line.
- No secret is a command flag.

## Startup

The server moves through `starting`, `awaiting_operator`, `ready`,
`closing`, or `failed`. It reports the state in `GET /health`, `GET /info`,
and the `server.state` event.

- The listener starts first, so clients can connect before the session
  exists. Requests that need the session return 503 until it is ready.
- Launch uses `SessionLauncher`, the same path as the terminal. When the
  workspace needs trust (the `workspaceTrust` switch applies) or `--resume`
  needs a choice, the state is `awaiting_operator` and the question is an
  interaction. A declined trust or session choice, or an invalid launch,
  sets `failed`, prints the reason on stderr, and exits with code 1.
- A saved session that another process has open cannot be resumed. The
  server enters the `failed` state with `Session is already open  <id>`, and
  `/resume` of such a session reports the same text and keeps the current one.
- Once listening, the server writes one JSON line to stdout for a wrapper:

  ```json
  {"event":"listening","url":"http://127.0.0.1:52144","pid":4242,"workspace":"/repo","discovery":"/home/me/.crystal/server/4242.json"}
  ```

  The line never carries a token. While the server runs it is the only line
  on stdout; the `stopped` line (see Shutdown) is the other.
- On stderr, for the operator, it then prints a summary: server URL, listen
  address, port, workspace, auth mode, the token (only a generated token, and
  only when stderr is a terminal; otherwise where to find it), the discovery
  file, the pid, and `Ctrl+C stops the server.` It also prints
  `Waiting for a client to answer a startup question`, `Session ready`,
  `Stopping  <reason>`, `Session saved  <id>`, and `Server stopped` as they
  happen.
- It writes the discovery file `~/.crystal/server/<pid>.json` with owner-only
  permissions in an owner-only directory: url, pid, workspace, build
  identity, and the token when the server generated it. The session id is not
  in the file because `/clear`, `/resume`, and `/fork` change it; read
  `GET /info`. The file is deleted on exit. A file left by a crashed process
  names a dead pid.

## Authentication

- `--auth token` is the default on every listener, including loopback and a
  Unix socket, because other local processes and browser pages can reach
  loopback.
- Token sources, first match wins: `CRYSTAL_SERVER_TOKEN`, `--token-file`
  (an owner-only file), `--token-stdin` (one line), otherwise a generated
  256-bit random token written to the discovery file and printed on stderr
  only when stderr is a terminal. A supplied token has at least 16
  characters. Tokens never appear in arguments, the listening line, or logs.
  Only the SHA-256 of each token is kept, and comparison is constant time.
- `--auth none` turns token checks off. Every request then has every scope.
  The server prints a warning at start. Host and Origin checks still apply.
- A non-loopback listener without TLS is allowed. The server prints a warning
  that tokens and session content travel in clear text.
- Requests send `Authorization: Bearer <token>`. `GET /health` needs no token
  and returns only the state.
- Scopes:
  - `observe`: snapshot, events, images, export, children, quick prompt list.
  - `operate`: input, interrupt, recall, mode, verbose, draft, image upload,
    quick prompt send, side question control, interaction answers, and the
    slash commands that only report or act inside the session: `/help`,
    `/status`, `/stats`, `/btw`, `/clear`, `/fork`, `/child`, `/sessions`,
    `/compact` and `/compact full`, `/todos`, `/attach`, `/export`, `/tools`,
    `/plugins`, `/skills`, `/notify`, and `/mcp` without arguments, `/mcp list|show`,
    `/cd` without a path, `/plan`, `/mode`, and plugin or MCP prompt commands.
  - `admin`: shutdown, token management, answering a `trust` interaction,
    and slash commands that change trust, directory access, approval, the
    model or its slots, prompt sets, rules, agents, the workspace, saved
    configuration, or quit the session (`/approval`, `/thinking`, `/context`,
    `/tier`, `/speed`, `/tokens`, `/model`, `/promptset`, `/promptattach`,
    `/statusline`, `/cd <path>`, `/space`, `/trust`, `/rules`, `/access`,
    `/resume`, `/agents`, `/verbose`, `/quit`, and the argument forms of
    `/compact`, `/todos`, `/tools`, `/skills`, `/plugins`, `/notify`, and `/mcp`).
    `/notify test` runs the operator's `operatorNotify` command on the server
    host, so it needs `admin`.
    `ServerScopes.ForVerb` throws for a verb it does not classify, and a test
    covers the whole enum.
- The startup token has every scope. `POST /auth/tokens` issues a narrower
  token (for example observe only) that lives for this process and can be
  revoked with `DELETE /auth/tokens/{id}`. Issued tokens are not persisted.
- Browsers cannot set headers on `EventSource`. `POST /auth/ticket` returns a
  single-use ticket valid for 30 seconds. Only `GET /events?ticket=` accepts
  it, with the scope of the token that asked for it. A long-lived token never
  goes in a URL.
- The `Host` header must name `localhost`, an IP address, or an
  `--allow-host` value, which blocks DNS rebinding; otherwise the response is
  421. An IP literal cannot be a name an attacker controls, so a server on
  `0.0.0.0` reached by a LAN address needs no `--allow-host`; a DNS name does.
  CORS is off unless `--allow-origin` lists an
  origin. A request with an `Origin` header that is not listed is rejected
  with 403, even with `--auth none`.
- `--allow-origin '*'` lists every origin. Each response then echoes the
  request's own `Origin`, so the Host check and token authentication still
  apply. The server prints a warning at start. With `--auth none` it leaves
  nothing between a web page and the workspace, so pair `*` with a token.
- CORS headers (`Access-Control-Allow-Origin`, `Vary: Origin`, and
  `Access-Control-Expose-Headers`) go on every response to a listed origin,
  including 401, 421, and 429, so a page can tell the cause apart. A rejected
  origin gets no CORS header, so a 403 for it is opaque to the page by design.
  A listed origin that reaches a disallowed `Host` gets a readable 421.
- `Access-Control-Expose-Headers` always names `Retry-After`,
  `Content-Disposition`, and `WWW-Authenticate`. `--expose-header <NAME>` and
  `server.exposeHeaders` add more.
- `--allow-private-network` (or `server.allowPrivateNetwork`) answers
  `Access-Control-Allow-Private-Network: true` to a preflight from a listed
  origin that sends `Access-Control-Request-Private-Network`. It is off by
  default; current Chrome asks the user instead of sending this preflight.
- `GET /health` goes through the same Host and Origin checks as every other
  route. It needs no token.
- `config.json` may hold the same lists under `server`: `allowOrigins`,
  `allowHosts`, `exposeHeaders`, and `allowPrivateNetwork`. Command-line
  values are added to the config values. Invalid entries stop startup.
- Twenty failed authentications from one address within a minute return 429
  for the rest of that minute. JSON bodies are limited to 1 MiB; image
  uploads keep the 20 MiB host limit.

## Conventions

- JSON with camelCase fields over UTF-8. Server-sent events for the stream.
- Every request carries `X-Crystal-Client: <random id>` chosen by the client
  for that page or window (letters, digits, `-`, `_`, `.`, at most 128). A
  browser stream that cannot set the header passes `?client=`. It routes
  targeted events and records which client started a request.
- A state-changing JSON request may carry `clientRequestId`; `POST /images`
  takes it as a query parameter. A repeat from the same client within ten
  minutes returns the first result without running again.
- Requests that need the live session return 503 before it is ready.
- Errors are `application/problem+json` with English text.
- Clients ignore event types and fields they do not know.

## Endpoints

| Route | Scope | Purpose |
| :--- | :--- | :--- |
| `GET /health` | none | State only |
| `GET /info` | observe | Build identity, capability lists (event types, operations, slash commands, interaction kinds), session id, workspace |
| `GET /snapshot` | observe | Session state, `lastSeq`, pending interactions, server state |
| `GET /events?after=<seq>` | observe | Event stream; honors `Last-Event-ID` |
| `POST /input` | operate | `{"text"}`: a prompt, a slash command, a follow-up, or empty text that releases the queue. Returns `accepted` (false when the engine refused the input), `pending` (a setting was recorded for the next turn), `error` (the refusal text or null), `turnActive`, and `quit` |
| `POST /interrupt` | operate | `TryInterrupt` |
| `POST /recall` | operate | `TryRecall`; the client states that its composer can take the message back |
| `POST /mode` | operate | `cycle`, `toggle-plan`, or `set` with a name. Returns `mode`, `planMode`, and `pending`. During a turn the change applies on the next turn |
| `POST /verbose` | operate | `{"target","enabled"}`: `SetVerbose` (Ctrl+O and Ctrl+G) |
| `PUT /draft` | operate | `{"text"}`: `NotifyDraftChanged` for this client's draft images |
| `POST /images` | operate | Raw image bytes: `TryAttachImage`; returns `number`, `mimeType`, `marker`, and `trustedMarker` to put in the submitted text |
| `GET /images/{number}` | observe | `TryGetImage`; bytes of a session or tool image |
| `GET /quick-prompts` | observe | The F6 catalog |
| `POST /quick-prompts/{name}/send` | operate | `SubmitQuickPromptAsync`; Fill stays in the client |
| `POST /side/cancel`, `POST /side/clear` | operate | Side question control; `/btw` itself goes through `/input` |
| `GET /children`, `GET /children/{id}` | observe | Child list, archive, events, usage, and todos (F7 and `/child`) |
| `GET /export?format=markdown\|json&system=true\|false` | observe | `TryExport` as a download; `/export` still writes on the server |
| `GET /interactions`, `POST /interactions/{id}` | operate | Pending interactions and answers |
| `POST /auth/ticket` | observe | Event-stream ticket |
| `GET /auth/tokens`, `POST /auth/tokens`, `DELETE /auth/tokens/{id}` | admin | List, issue (`scope`, optional `label`), and revoke process-local tokens; the startup token cannot be revoked |
| `POST /shutdown` | admin | Same as `/quit` |

## Events

Each event is one SSE message whose `id` is `seq`:

```json
{"seq":1042,"type":"stream.text","time":"2026-10-26T02:36:20Z","sessionId":"…",
 "origin":"client-7f3a","target":"client-7f3a",
 "source":{"childSessionId":"…","invocationId":"…"},
 "data":{"delta":"…"}}
```

`sessionId` is null before the session exists. `origin` names the client
whose request produced the event. `target` appears only on events meant for
one client. `source` appears only on child events: `ChildEventReceived` is
unwrapped, so a child's text arrives as `stream.text` with a `source`. Every
`SessionEvent` and every Crystal `ChatStreamEvent` maps to one type:

| Engine event | Type |
| :--- | :--- |
| `SessionStarted`, `ChromeChanged` | `session.started`, `chrome.changed` |
| `PreferencesChanged`, `StatusFieldsChanged` | `preferences.changed`, `status-fields.changed` |
| `SessionOptionsChanged` | `options.changed`: each option's `id`, `name`, `category`, `current`, `switch`, and `values` (`value`, `name`, the slash `command` that selects it, optional `description` and `group`). An empty `command` cannot be chosen directly. Send the command through `POST /input` |
| `SlashCommandsChanged`, `PromptHistoryLoaded` | `commands.changed`, `prompt-history.loaded` |
| `ImageHistoryInvalidated`, `ActivityChanged` | `images.invalidated`, `activity.changed` |
| `LoadingProgressed` | `loading.progressed`: `kind` (`tool_set`, `plugin`, or `mcp_server`) and `name`, sent while `activity.changed` reports a loading activity. Not part of the snapshot |
| `UserMessageSent`, `UserMessageRecalled` | `message.sent`, `message.recalled` |
| `TurnStarted`, `TurnFinished`, `ModelRoundClosed` | `turn.started`, `turn.finished`, `round.closed` |
| `StreamReceived` | `stream.text`, `stream.reasoning`, `stream.reasoning-state`, `stream.tool-call`, `stream.usage`, `stream.completed` |
| `ToolCallsIssued`, `ToolResultsReceived` | `tools.called`, `tools.results` |
| `RetryScheduled`, `UsageChanged`, `ContextEstimated` | `retry.scheduled`, `usage.changed`, `context.estimated` |
| `QueueChanged`, `TodosChanged` | `queue.changed`, `todos.changed` |
| `HistoryReplayed`, `ConversationCleared` | `conversation.replayed`, `conversation.cleared` |
| `CompactionStarted`, `CompactionFinished` | `compaction.started` (`id`, `manual`), `compaction.finished` (`id`, `status`: `completed`, `unchanged`, `failed`, or `cancelled`; `summary`, the stored summary of a completed compaction or null; `shown`, whether the operator's summary display setting draws it) |
| `NoteWritten`, `ErrorWritten` | `note`, `error` |
| `HelpRequested`, `StatusReported`, `StatsReported`, `ToolsListed`, `TodosListed`, `DocumentWritten` | `report.help`, `report.status`, `report.stats`, `report.tools`, `report.todos`, `report.document` (`document`: `title` and `blocks`, each with `type` of `paragraph`, `keyValues`, `table`, or `list`, plus `columns`, `rows`, `lines`, and `omitted`) |
| `SideQuestionSnapshot` | `side.changed` |
| `ApprovalPassed`, `ApprovalReviewing` | `approval.passed`, `approval.reviewing` (with stage `primary` or `extended`) |
| `ChildSessionChanged`, `ChildViewRequested` | `child.changed`, `child.view` |

The chrome object in `session.started`, `chrome.changed`, and the snapshot
names the active `provider` and `model` along with the `sessionId`, workspace, mode, approval,
thinking, prompt set, context window, service tier, and speed. Its `pending`
object lists settings chosen during a turn that apply on the next turn:
`provider` and `model` together, `contextWindow`, `serviceTier`, `speed`
(`unset` clears the slot), `thinking`, `mode`, and `approval`. A key appears
only while that setting waits. `report.status` carries the same `pending`.

Server events: `server.state`, `server.closing` (with the session id and
whether it was saved), `interaction.opened`, `interaction.resolved`,
`interaction.cancelled`, and `resync`.

`stream.reasoning-state` reports that opaque provider state arrived, with its
format name only. Image bytes never appear in events: trusted markers are
written as plain `[Image #N]` and an `images` array lists the numbers to
fetch from `GET /images/{number}`. `message.recalled` also carries the raw
`draft` with trusted markers, so the sending client can put it back in its
composer.

Delivery: state events go to every client. Reports, `child.view`, and the
notes and errors a request produced go only to the client that sent that
request. A running turn, a child, and a queued follow-up have no origin, so
everything they produce goes to every client.

## Snapshot

`GET /snapshot` returns the state a client needs to draw the session without
replaying history: `state`, `sessionId`, chrome, preferences, slash
commands, prompt history, `conversation` (messages, readable reasoning, tool
calls, and tool results), `live` (the in-progress model round: text,
reasoning, tool-call arguments so far, retries), `turnActive`, activity,
queue, todos, status fields, usage, context window and estimate, the
side-question thread, child summaries, the last turn's stop reason and
counts, `approvals` (each call that passed approval in this conversation,
with the same fields as `approval.passed`), the pending interactions this
client may answer, and `lastSeq`. A compaction summary is a `system` message
in `conversation` whether or not it was shown, and prompt history drops
entries with image markers after `images.invalidated`. Display settings do
not change what the snapshot holds. The
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
| `question` | `IUserPrompt.AskAsync` | `{"answers":[["label"],…]}` in question order, custom text where a question allows it, or `{"dismissed":true}` |
| `trust` | `IWorkspaceTrustPrompt.ConfirmAsync` | `{"trust":true\|false}`; needs admin |
| `choose_session` | `ISessionChooser.ChooseAsync` | `{"sessionId":"…"}` or `{"sessionId":null}` to decline |
| `choose_child` | `SessionFrontEnd.ChooseChildAsync` | `{"childId":"…"}` or `{"childId":null}` to decline |

- An approval payload carries the call, the classification (a child's call
  has a summary prefixed with the child), the primary review verdict and the
  extended review verdict when they ran, and the allowed `choices`. The
  answer is `{"choice":"once|session|always|deny"}`.
- An answer the engine did not offer is rejected with 400 and the
  interaction stays open. An answer to an interaction that is gone or
  belongs to another client returns 409.
- Interactions raised by the model (approval, question) go to every client
  with `operate`. The first valid answer wins and the rest receive
  `interaction.resolved`.
- Interactions raised by a client command (`choose_session` from `/resume`,
  `choose_child` from `/child`, `trust` from `/cd`) go to that client. If it
  disconnects, they go to every client with `operate`.
- A cancelled turn cancels its interactions with `interaction.cancelled`.
  Otherwise an interaction waits, as the terminal card does. Shutdown
  cancels every interaction; the engine treats that as declined.

## Refresh and reconnect

- Events also go into a ring buffer bounded by count and bytes.
- Refresh: `GET /snapshot`, then `GET /events?after=lastSeq`.
- Reconnect: the browser sends `Last-Event-ID`. When that cursor has left the
  buffer the server sends `resync` and closes the stream; the client fetches a
  new snapshot.
- The stream sends a comment heartbeat every 15 seconds and `retry: 2000`. A
  slow client falls behind on its own cursor and never blocks the engine.
- `/clear`, `/resume`, and `/fork` change the session id. Every later
  envelope carries the new id, and `conversation.cleared` or
  `conversation.replayed` tells the client to redraw.

## Shutdown

`/quit`, `POST /shutdown`, Ctrl+C (SIGINT), SIGTERM, SIGQUIT, or stdin
closing with `--exit-on-stdin-close` start shutdown, also while the server
is still waiting on a startup question:

1. `server.state` with `state: closing` and a `reason` goes out at once,
   before a running turn has unwound. Reasons are `interrupt`, `terminate`,
   `quit_signal`, `request`, `quit`, `stdin_closed`, and `cancelled`. The
   first reason wins.
2. The turn is interrupted and collected without starting queued work, and
   pending interactions get `interaction.cancelled`.
3. `CloseAsync` runs, and `server.closing` reports the session id, whether it
   was saved, and the same `reason`. Before the session existed there is no
   `server.closing`.
4. The last stdout line is written for a wrapper:

   ```json
   {"event":"stopped","pid":4242,"exitCode":0,"reason":"interrupt","sessionId":"…","saved":true}
   ```

   `reason` is the stop reason, or `failed` with an `error` field when the
   launch failed. `sessionId` is null when no session was opened.
5. Every stream ends, the listener stops, and the discovery file and socket
   are deleted. The exit code is 0; a failed launch exits with 1.

A second Ctrl+C while the server is stopping ends the process at once.

## Parity guards

`CrystalCode.Server.Tests` fails the build when:

- a public `CodingSession` member or a `SessionFrontEnd` contract has no
  endpoint or interaction and no recorded exclusion
  (`PasteClipboardImageAsync` is excluded: it reads the server's clipboard;
  `POST /images` replaces it);
- a `SessionEvent` subclass or a Crystal `ChatStreamEvent` has no wire type;
- a `SessionVerb` has no scope;
- a provider with a literal API key leaks that key into the snapshot, the
  stream, or `/info` after `/tools`, `/status full`, `/stats`, `/help`,
  `/model`, and `/todos`;
- the server assembly references the terminal, Spectre.Console,
  Terminal.Gui, or `System.Console`;
- a route or event type carries a version identifier.

## Not in scope

- Several active sessions in one server process, and several concurrent
  tasks in one terminal process. Both are deferred in `TODO.md`. Plugin and
  external tool load contexts are not collectible, so a long-lived process
  that loaded them per session would only grow.
- MCP servers supplied by a front end for one session (see MCP gaps in
  `TODO.md`).
- The front ends themselves.
