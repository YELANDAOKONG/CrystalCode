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
  undecided. Extending, adjusting, or disabling a built-in tool is the
  high-priority item below, not this one.
- Switchable thinking-chain compression. After a thinking pass, a model
  summarizes that thinking. It is not context compaction, microcompaction,
  `verboseThinking`, or `thinkingDisplay`.
- Configurable serial or concurrent execution of ordinary tool-call batches.
  Ordinary Work and Plan tools remain serial. Adjacent child `task` groups
  use their own bounded concurrency.

## High priority

These are part of the product direction and are not in the current build.
The constraints are in `BUSINESS.md` under Deferred product work. Current
behavior stands until a specific change defines otherwise.

- Extensible and adjustable built-in tools. Explicit in-process `IPlugin`
  replacement is implemented: `Append` keeps duplicate-name rejection, while
  `Replace` names a reserved built-in and may target Plan or Work independently.
  The replacement keeps the built-in model-facing name and uses the host
  executor and approval path. Remaining work is session-only disablement,
  disk-plugin and external-tool replacement, metadata adjustment, and other
  disable rules. Host-owned numeric limits stay under Pending evaluation.
- Product modes other than Plan and Work, including operator-defined custom
  modes. The product has exactly two modes today. Prompt overlays, prompt
  sets, prompt attachments, and external tool catalogs do not add a mode.
  Behavior and ownership are not defined. No mode registry, configuration
  field, or public type is reserved.
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
- MCP servers. Do not bypass the existing plugin, approval, and media
  boundaries.
- The Agent Client Protocol (ACP). The working name is crystal agent. It
  would be a front end that implements `SessionFrontEnd`, not an engine
  protocol. Do not bypass approval, trust, or the tool boundary. ACP does
  not speak MCP itself. Editor-supplied MCP servers, when that front end
  exists, use the same MCP client as local servers. The terminal remains the
  only operator surface until it exists. `crystal agent` is not a command,
  project, or public type in this build.
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
- Child Agent configuration. Current `/agents` settings, the `agents` object
  in `config.json`, process `--agents` and `--agents-concurrency`, and custom
  `agent.json` manifests stand. Which settings change, and at which scope,
  are not defined. Do not reserve a new configuration field or command. This
  is not a new Agent, not a mode, and not separate working copies or a direct
  operator conversation with a child.
- Provider-specific custom-format tools, such as OpenAI's `apply_patch`.
  These tools take freeform text input instead of JSON arguments, and the
  provider defines the format. The direction is to register and execute them
  through a Crystal chat contract that carries text-input tools. Which
  providers and tools participate, how a tool is declared, and how approval,
  path classification, and errors work on freeform input are not defined. Do
  not reserve a configuration field, command, public type, or tool-set
  format. This item does not authorize changes under `../Crystal`. Provider
  adapters still implement only Crystal chat contracts and do not own tools.
  This is not the high-priority item for extending, adjusting, or disabling a
  built-in tool.
- Separate provider files. The direction is to define providers across more
  than one file instead of only the `providers` object embedded in
  `config.json`. Which files are read, their scope, precedence, merge and
  override behavior, write policy, and trust semantics are not defined. Do
  not reserve a file path, configuration field, or public type. This is not
  editing provider definitions in the TUI, and it is not workspace-level
  configuration.

## Low priority

None. Do not invent a priority.

## Pending evaluation

These are under consideration or not decided. They are not a priority and
are not part of parent/child Agents. Current behavior stands. Do not change
it speculatively, and do not reserve types or configuration for it.

Operator-defined modes other than Plan and Work are no longer in this
bucket. That decision is recorded under High priority.

- Whether Plan may include bash. Not decided. Built-in Plan has no edit,
  write, or bash.
- Whether built-in tool limits become operator configuration. Not decided.
  Result size caps, read, grep, glob, search, list, and tree limits,
  background task counts and log bounds, the image size limit, and the todo
  caps are host-owned constants. Timeouts and turn budgets are already
  configurable and are not part of this question. A later decision must
  define which limits may vary, at which scope, and how a larger limit
  interacts with context compaction. This is not the high-priority item for
  extending, adjusting, or disabling a built-in tool.
- More hook methods, added later on the existing hook interfaces. They are
  not reserved as empty types.
- Whether plugins declare dependencies on one another, and related loading
  questions. Not decided, and not part of the deferred list until a decision
  is made.
