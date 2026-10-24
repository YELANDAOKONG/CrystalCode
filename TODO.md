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
  build. Not in it: sampling, OAuth for remote servers, image and audio
  results (they are named, not sent), servers passed in by a front end for one
  session, and URL-mode elicitation. Do not bypass the existing plugin,
  approval, and media boundaries.
- The Agent Client Protocol (ACP). The working name is crystal agent. It
  would be a front end that implements `SessionFrontEnd`, not an engine
  protocol. Do not bypass approval, trust, or the tool boundary. ACP does
  not speak MCP itself. Editor-supplied MCP servers, when that front end
  exists, use the same MCP client as local servers. The terminal remains the
  only operator surface until it exists. `crystal agent` is not a command,
  project, or public type in this build.
- An agent API server. The working name is crystal server. It would run in a
  workspace and expose the full Crystal Code capability over our own
  protocol, so that other front ends can drive it. The operator picks the
  workspace and the other session parameters when starting it. It would be a
  front end that implements `SessionFrontEnd`, not an engine protocol, and
  not ACP. The wire format, transport, authentication, how approval and
  questions reach the connected front end, and how many sessions one server
  holds are not defined. Do not bypass approval, trust, the workspace fence,
  or `ToolInvocationPolicy`. Do not put secrets on the command line or
  persist its flags to `config.json`. The terminal remains the only operator
  surface until it exists. `crystal server` is not a command, project, or
  public type in this build.
- Refactoring and optimizing `crystal run`. It remains the headless entry
  for one task in this executable, not an operator surface and not a second
  assembly. The work must not put secrets on the command line, persist flags
  to `config.json`, or make the run ask the operator.

## Medium priority

These are part of the product direction and are not in the current build.
The constraints are in `BUSINESS.md` under Deferred product work. Current
behavior stands until a specific change defines otherwise.

- Automatically fetching model-list information from a provider. `/model`
  still selects only models listed in the configured catalog.
- A second additional review of the current product. It does not change
  product scope, reserve a public type, command, or configuration field, or
  authorize new behavior. Findings stay inside the boundaries in
  `BUSINESS.md` and `ARCHITECTURE.md`. It is not the Review or Audit
  approval mode, and it does not change those modes.
- A full code review of the whole codebase. It examines existing code; it
  does not change product scope, reserve a public type, command, or
  configuration field, or authorize new behavior. Scope, depth, and how
  findings are tracked are not defined. Findings stay inside the boundaries
  in `BUSINESS.md` and `ARCHITECTURE.md`. It is not the Review or Audit
  approval mode, and it does not change those modes. It is not the second
  additional review above.
- Child Agent configuration. Current `/agents` settings, the `agents` object
  in `config.json`, process `--agents` and `--agents-concurrency`, and custom
  `agent.json` manifests stand. Which settings change, and at which scope,
  are not defined. Do not reserve a new configuration field or command. This
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
