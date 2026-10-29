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

- Session settings changed during a turn take effect inconsistently, and
  front ends do not say so. Checked with a headless session while a turn ran
  (`SessionRunner`, blocking model):
  - `/plan`, `/approval`, `/approval <level>`, and Tab (`CycleMode`,
    `TogglePlan`) succeed at once. The turn keeps the tool executor, approval
    policy, and child Plan ceiling it started with. Only the system message is
    replaced in place, so a Plan switch can show Plan while the running turn
    still has Work tools. The new approval level is written to `config.json`
    at once.
  - `/mode <name>`, `/tier`, `/speed`, `/context`, `/model`, `/cd`, and
    `/agents` changes are refused with `Finish the current turn before ...`.
  - `/thinking` has no turn check in `ChangeThinking`. The reasoning options
    are read when the turn starts, so it applies next turn, but the command
    still saves and rebuilds executors mid-turn. Not exercised with a
    thinking-capable model.
  - `crystal bridge`: a refused command returns `-32603` with
    `command_refused`, but a change that succeeds mid-turn also returns
    success, so an editor cannot tell it applies later or only partly. The
    `approval` option, `_crystal/mode/cycle`, and `_crystal/mode/togglePlan`
    do this. Only `model` has a busy-refusal test; `tier`, `speed`, and
    `context` use the same path and were not checked one by one.
  - `crystal server`: `POST /input` answers 200 with `turnActive` and `quit`
    even when the engine refused the command. The refusal reaches the client
    only as an `error` event on `/events`. `POST /mode` with `set` calls
    `TrySetMode`, which has no turn check, so it succeeds where the terminal
    and Bridge refuse. Approval, thinking, tier, and speed have no route of
    their own and go through `/input`.
  - Not decided: whether mode, approval, thinking, tier, speed, context, and
    model all become "applies next turn" (recorded during a turn, applied when
    it ends, before a queued follow-up starts), when `config.json` is
    written, whether chrome and Bridge `config_option_update` show the old or
    the pending value, and whether `POST /input` reports the refusal in its
    response. Do not reserve an event, wire field, or `_meta.crystal` key for
    it. A decision updates ARCHITECTURE.md and the busy-command text in
    `docs/user-guide.md`.

## Deferred

These are part of the product direction and are not in the current build.
The constraints are in `BUSINESS.md` under Deferred product work.

- An operating-system sandbox.
- Provider protocols other than DeepSeek and OpenAI-compatible Chat
  Completions, OpenAI Responses, Anthropic Messages, Gemini GenerateContent,
  and Ollama Chat.
- Audio and video input, and audio or video model output. A stored
  `audioInput` or `videoInput` declaration is not that path. Image model
  output is under High priority (Image generation).
- Editing provider definitions in the TUI.
- Workspace-level configuration at `<workspace>/.crystal/config.json`. Field
  scope, precedence, write policy, and trust semantics are not defined. A
  repository-controlled file must not weaken approval or trust.
- A module system below plugins and raw hooks, once its behavior and
  ownership are defined.
- More plugin capabilities beyond the current contribution surface.
  An example is custom tool execution modes. The set is not closed.
  Running plugin process commands (`crystal <name>`) is deferred: the
  contract and registration exist, but the executable has no runner, and it
  must not load plugins for arbitrary first arguments or run project plugins
  for an untrusted workspace. Do not
  reserve a flag, configuration field, or public type. A plugin process
  command, an in-session slash command, `crystal plugins`, and
  `crystal tools` are not this path. A custom
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
- Image generation. Letting the session produce images, whether through a
  tool or as native model output. Which path comes first, which providers and
  protocols, how a model declares it, how a generated image is stored,
  replayed, resumed, forked, and exported, and how approval and cost are
  handled are not defined. Generated images must keep the existing image
  boundary: signature check, size limit, owner-only media store, and
  `[Image #N]` markers, with no bytes in logs or composer text. Do not reserve
  a configuration field, command, public type, event, wire field, or tool-set
  format. It does not authorize changes under `../Crystal`, and approval,
  trust, the workspace fence, and `ToolInvocationPolicy` stay as they are.
- A built-in web search tool. A host-executed tool that lets the model search
  the web and read the results. The search backend, whether a separate page
  fetch tool exists, how approval and network access are classified, result
  size bounds, and failure behavior are not defined. It is not a provider's
  server-side web search, not an MCP server, and not a plugin or tool set.
  Results are untrusted data, not instructions. Do not reserve a
  configuration field, command, public type, or backend name, and do not
  store a search key in source, logs, or on a command line. Approval, trust,
  the workspace fence, and `ToolInvocationPolicy` stay as they are.

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

These are part of the product direction and are not in the current build.
The constraints are in `BUSINESS.md` under Deferred product work. Current
behavior stands until a specific change defines otherwise.

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

  Known input to that work: the release archives have different layouts.
  `.github/workflows/release.yml` zips the `publish` directory itself on
  macOS and Linux (entries are `publish/CrystalCode`), while on Windows
  `Compress-Archive -Path .\publish\*` puts files at the archive root. The
  install scripts tolerate both because they search for the binary
  recursively. A package manager manifest that names a fixed path inside the
  archive would not. Whether to unify the layout is not decided.

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
