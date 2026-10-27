# Deferred work

> **CrystalCode stays a general tool. It is not bound to one vendor or one protocol. We are still weighing that against the protocols already common in the ecosystem.

This file only assigns a bucket. [BUSINESS.md](BUSINESS.md) owns what each item
means and what must not happen. An entry here is not permission to implement
it, and it does not reserve a public type, command, configuration field, or
project.

Nothing is scheduled. A bucket records priority only. Moving an item still
does not authorize implementation until `BUSINESS.md` defines its behavior
and ownership.

Pending evaluation is not a priority. Whether to do those items is not
decided. Do not fold one into a deferred, high-priority, or
medium-priority item.

Issues are separate from the buckets. An issue is a defect in current behavior
that is tracked here until it is fixed. It is not a scope change and has no
priority. A fix is not decided by being listed. Remove the entry in the change
that fixes it.

## Issues

- Waiting .NET 11: a `bash` or `bash_start` command that asks for a password
  breaks the terminal UI. Examples are `git push` over HTTPS or SSH, `ssh`,
  and `sudo`. Stdin is already redirected and closed (ARCHITECTURE.md,
  Background Bash tasks), but these programs open `/dev/tty` directly instead
  of reading stdin. The child inherits the controlling terminal of the TUI
  process, so its prompt and the operator's keystrokes bypass the TUI and
  corrupt the screen. Confirmed on Unix only with a pty experiment: a child
  can write to `/dev/tty`, and a child in a new session cannot. Not yet
  checked: Windows behavior, such as Git Bash, Git Credential Manager, and
  console-attached prompts.

  The intended fix waits for .NET 11, where `ProcessStartInfo.StartDetached`
  detaches the child without an external command. The project targets net10.0
  today. Whether to ship an interim fix is not decided. Candidates are
  environment variables for git and ssh (`GIT_TERMINAL_PROMPT`,
  `GIT_ASKPASS`, `SSH_ASKPASS_REQUIRE`, `GCM_INTERACTIVE`), which cover only
  those programs, and a note in the `bash` tool description. Any fix must work
  on Windows, macOS, and Linux and must not add an external command
  dependency.
- Loading tools freezes the terminal UI. The operator reports that the whole
  UI stops while `Loading Tools` is shown. Cause found by reading the code,
  not yet measured: `TerminalHost.RunLoopAsync` awaits `CodingSession.StartAsync`
  without the key pump, so no spinner, resize, or key handling runs during
  startup. `ReloadExternalToolsWithProgress` also runs
  `ExternalCatalog.Load` synchronously on the calling thread (directory scan,
  assembly load, tool construction), so even a pumped caller would not get
  control back before the load ends. MCP connection in the same startup path
  is asynchronous but is not pumped either. The same synchronous reload runs
  at `/tools approval`, `/tools on|off|reload`, tool set enable and disable,
  `/plugins`, plugin enable and disable, and every workspace change (`/cd`,
  `/space`, resume into another workspace). The `Ctrl+C` handler in
  `RunLoopAsync` is registered after `StartAsync`, so an interrupt during
  startup is not handled by it.

  The fix is not decided. Constraints for it: the UI must keep painting and
  must not turn keystrokes typed during loading into errors, queued turns, or
  lost drafts; `CrystalCode.Engine` stays front-end neutral and reports
  progress only through `SessionEvent`; `crystal server` and `crystal bridge`
  start their runner after `StartAsync` and must keep their behavior. Candidate
  direction: load the catalog off the calling thread with cancellation, pump
  the terminal during `StartAsync`, and hold prompt submission while the
  activity is `LoadingTools` or `LoadingPlugins` with the draft kept. Whether
  the in-session reload paths are fixed in the same change is not decided.

## Deferred

These are part of the product direction and are not in the current build.
The constraints are in `BUSINESS.md` under Deferred product work.

- An operating-system sandbox.
- Provider protocols other than DeepSeek and OpenAI-compatible Chat
  Completions, OpenAI Responses, Anthropic Messages, Gemini GenerateContent,
  and Ollama Chat.
- Audio and video input, and image, audio, or video model output. A stored
  `audioInput` or `videoInput` declaration is not that path.
- Editing provider definitions in the TUI.
- Workspace-level configuration at `<workspace>/.crystal/config.json`. Field
  scope, precedence, write policy, and trust semantics are not defined. A
  repository-controlled file must not weaken approval or trust.
- A module system below plugins and raw hooks, once its behavior and
  ownership are defined.
- More plugin capabilities beyond the current contribution surface.
  Examples include extending the process command line and custom tool
  execution modes. The set is not closed. Do not reserve a command, flag,
  configuration field, or public type. An in-session slash command,
  `crystal plugins`, and `crystal tools` are not this path. A custom
  execution mode is not the deferred host setting for serial or concurrent
  ordinary tool batches. Pending hook methods and plugin dependencies stay
  undecided.
- Switchable thinking-chain compression. After a thinking pass, a model
  summarizes that thinking. It is not context compaction, microcompaction,
  `verboseThinking`, or `thinkingDisplay`.
- Configurable serial or concurrent execution of ordinary tool-call batches.
  Ordinary Work and Plan tools remain serial. Adjacent child `task` groups
  use their own bounded concurrency.
- OAuth sign-in for model providers, such as a ChatGPT account login for
  OpenAI. Today a provider authenticates only with a static API key. Whether
  it is built, which providers, the token store, and refresh behavior are not
  defined. Do not reserve a command, configuration field, or public type. It
  is not OAuth for remote MCP servers (see MCP gaps).
- Several concurrent tasks in one terminal process.
- Several active sessions in one `crystal server` process. Plugin and
  external tool load contexts are not collectible, so this needs that
  question answered first.

## High priority

These are part of the product direction and are not in the current build.
The constraints are in `BUSINESS.md` under Deferred product work. Current
behavior stands until a specific change defines otherwise.

- Publishing a NuGet package. Package id, which assemblies ship, and whether
  a library is also published are not defined. Do not reserve a package id or
  add a project. This item is not permission to add or update a package
  reference. Current release artifacts and install scripts remain the install
  path.
- .NET CLI tool support. Shipping the executable as a `dotnet tool` so that
  `dotnet tool install` places it on `PATH` with an installed command name.
  Package id, target frameworks, and which assemblies ship are not defined.
  Do not reserve a package id, project, command, or package reference. The
  installed tool stays this executable, not a second assembly, and
  `crystal run` keeps its current entry semantics.
- Other packaging support beyond the self-contained archives and install
  scripts. Channels are not selected, and package names, ownership, signing,
  update behavior, and platform coverage are not defined. Do not reserve a
  package name, tap, formula, image, or repository.
- MCP gaps. MCP tools, resources, prompts, elicitation, and roots are in this
  build, and so are servers a front end passes in for one session. Not in it:
  sampling, OAuth for remote servers, image and audio results (they are
  named, not sent), and URL-mode elicitation. Do not bypass the existing
  plugin, approval, and media boundaries.
- Refactoring and optimizing `crystal run`. It remains the headless entry
  for one task in this executable, not an operator surface and not a second
  assembly. This includes optimizing the JSON structure of its JSON output.
  Turn usage totals, call-id pairing, image-marker text, child event shapes,
  and the faulted-turn exit are defined in ARCHITECTURE.md. Still not
  defined: per-round step boundaries, approval and review events, a
  machine-readable final status and exit code, and any versioning or
  compatibility policy. Do not reserve a field, schema, or flag for them.
  The work must not put secrets on the command line, persist flags to
  `config.json`, or make the run ask the operator.
- Session state completeness for `crystal server`. A reconnecting or second
  client reads `SessionState`, which drops some of what the terminal shows.
  Known gaps: a compaction summary is only a one-shot note and reaches the
  archive only while a summary display setting is on, so the snapshot
  conversation, resume, and fork can lack it; and `ApprovalPassed` is not kept,
  so approval records vanish on refresh. Not yet examined: whether
  `ImageHistoryInvalidated` leaves stale prompt-history entries in the
  snapshot, and whether a child compaction summary is kept. Which events are
  session state and which are replies to one client is not fully defined.
  Display settings should decide only what a front end draws, not what the
  event stream or archive records. Do not reserve an event, a wire field, or a
  public type for it. When it is done, the server facts the client depends on
  are recorded in ARCHITECTURE.md.

## Medium priority

These are part of the product direction and are not in the current build.
The constraints are in `BUSINESS.md` under Deferred product work. Current
behavior stands until a specific change defines otherwise.

- Automatically fetching model-list information from a provider. `/model`
  still selects only models listed in the configured catalog.
- A full code review of the whole codebase. It examines existing code; it
  does not change product scope, reserve a public type, command, or
  configuration field, or authorize new behavior. Scope, depth, and how
  findings are tracked are not defined. Findings stay inside the boundaries
  in `BUSINESS.md` and `ARCHITECTURE.md`. It is not the Review or Audit
  approval mode, and it does not change those modes.
- Child Agent configuration. Current `/agents` settings, the `agents` object
  in `config.json`, process `--agents`, `--agents-concurrency`, and
  `--agents-model-calls`, `--agents-tool-calls`, `--agents-duration`, and custom
  `agent.json` manifests stand, as does `agents.limits` (per-run child
  model-call, tool-call, and duration bounds). Which other settings change,
  and at which scope, are not defined. Do not reserve a new configuration field or command. This
  is not a new Agent, not a mode, and not separate working copies or a direct
  operator conversation with a child.
- Custom-format tools beyond the one built-in case. A model that declares
  `textTools` is offered `apply_patch` as a free-form custom tool, so it
  writes the patch directly instead of wrapping it in JSON (see Data). That
  is the only tool and the only protocol, the Responses protocol, that does
  this. Other free-form tools, other protocols, tools that take more than one
  string, and operator or plugin tools that take free-form text are not
  defined. Do not reserve a command, public type, or tool-set format for
  them. This item does not authorize changes under `../Crystal`. Provider
  adapters still implement only Crystal chat contracts and do not own tools.
- A built-in web search tool. A host-executed tool that lets the model search
  the web and read the results. The search backend, whether a separate page
  fetch tool exists, how approval and network access are classified, result
  size bounds, and failure behavior are not defined. It is not a provider's
  server-side web search, not an MCP server, and not a plugin or tool set.
  Results are untrusted data, not instructions. Do not reserve a
  configuration field, command, public type, or backend name, and do not
  store a search key in source, logs, or on a command line. Approval, trust,
  the workspace fence, and `ToolInvocationPolicy` stay as they are.
- Built-in tool optimization. Improving how the built-in tools behave and
  perform. Which tools and which improvements are not defined. It is not a
  new tool, not operator configuration of tool limits (that question is under
  Pending evaluation), and not a change to approval, trust, or the workspace
  fence. Do not reserve a configuration field, command, or public type.
- Bridge model choices in editors that flatten groups. Model names from
  different providers can look identical in an editor that ignores ACP
  select groups. Whether and how the label names the provider is not
  defined. It is a Bridge wire change only. Do not reserve a configuration
  field or public type.

## Low priority

None. Do not invent a priority.

## Pending evaluation

These are under consideration or not decided. They are not a priority and
are not part of parent/child Agents. Current behavior stands. Do not change
it speculatively, and do not reserve types or configuration for it.

- Whether Plan may include bash. Not decided. Built-in Plan has no edit,
  write, or bash.
- Whether built-in tool limits become operator configuration. Not decided.
  Result size caps, read, grep, glob, search, list, and tree limits,
  background task counts and log bounds, the image size limit, and the todo
  caps are host-owned constants. Timeouts and turn budgets are already
  configurable and are not part of this question. A later decision must
  define which limits may vary, at which scope, and how a larger limit
  interacts with context compaction.
- A `/debug` option. Not decided, including whether it exists and what it
  covers. Do not reserve a command, configuration field, or public type for it.
- More hook methods, added later on the existing hook interfaces. They are
  not reserved as empty types.
- Whether plugins declare dependencies on one another, and related loading
  questions. Not decided, and not part of the deferred list until a decision
  is made.
- A `crystal server-manager` API host and session manager that owns and
  supervises its own child `crystal` processes. The process model, session
  lifecycle, discovery and authentication, child-process isolation and
  shutdown, restart behavior, resource limits, persistence, and API contract
  are not defined. It must not be treated as permission to add a command,
  project, public type, wire field, or server behavior until those boundaries
  are decided.
