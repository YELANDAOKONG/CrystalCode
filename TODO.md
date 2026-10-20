# Deferred work

> **CrystalCode stays a general tool. It is not bound to one vendor or one protocol. We are still weighing that against the protocols already common in the ecosystem.

This file only assigns a bucket. [BUSINESS.md](BUSINESS.md) owns what each item
means and what must not happen. An entry here is not permission to implement
it, and it does not reserve a public type, command, configuration field, or
project.

Nothing is scheduled. Medium and low priority have no items until a
decision recorded in this file moves one. Moving an item still does not
authorize implementation until `BUSINESS.md` defines its behavior and
ownership.

Pending evaluation is not a priority. Whether to do those items is not
decided. Do not fold one into a deferred or high-priority item.

## Deferred

These are part of the product direction and are not in the current build.
The constraints are in `BUSINESS.md` under Deferred product work.

- MCP servers. Do not bypass the existing plugin, approval, and media
  boundaries.
- The Agent Client Protocol (ACP). It would be a front end that implements
  `SessionFrontEnd`, not an engine protocol. Do not bypass approval, trust,
  or the tool boundary. ACP does not speak MCP itself. Editor-supplied MCP
  servers, when that front end exists, use the same MCP client as local
  servers. The terminal remains the only operator surface until it exists.
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
- Automatically fetching model-list information from a provider.
- A module system below plugins and raw hooks, once its behavior and
  ownership are defined.
- Saved, session, and plugin ways to disable a built-in tool, and supplying
  another tool in its place.
- Switchable thinking-chain compression. After a thinking pass, a model
  summarizes that thinking. It is not context compaction, microcompaction,
  `verboseThinking`, or `thinkingDisplay`.
- Configurable serial or concurrent execution of one tool-call batch. Work
  and Plan both run every batch serially today.

## High priority

- Parent/child Agents through `Crystal.Harness.AgentHarness`. Not in the
  current build. When implemented, the host consumes `AgentHarness` for
  named invocation ancestry and shared budgets. This product keeps prompts,
  tools, approval, persistence, and the terminal projection. This is not a
  new product mode. It does not decide Plan bash, custom modes, tool-limit
  configuration, plugin dependencies, or further hook methods.

## Medium priority

None. Do not invent a priority.

## Low priority

None. Do not invent a priority.

## Pending evaluation

These are under consideration or not decided. They are not a priority and
are not part of parent/child Agents. Current behavior stands. Do not change
it speculatively, and do not reserve types or configuration for it.

- Product modes other than Plan and Work, including operator-defined custom
  modes. Whether to add them is not decided. No mode registry is reserved.
  Prompt overlays, prompt sets, prompt attachments, and external tool
  catalogs do not add a mode.
- Whether Plan may include bash. Not decided. Built-in Plan has no edit,
  write, or bash.
- Whether built-in tool limits become operator configuration. Not decided.
  Result size caps, read, grep, glob, search, list, and tree limits,
  background task counts and log bounds, the image size limit, and the todo
  caps are host-owned constants. Timeouts and turn budgets are already
  configurable and are not part of this question. A later decision must
  define which limits may vary, at which scope, and how a larger limit
  interacts with context compaction.
- More hook methods, added later on the existing hook interfaces. They are
  not reserved as empty types.
- Whether plugins declare dependencies on one another, and related loading
  questions. Not decided, and not part of the deferred list until a decision
  is made.
