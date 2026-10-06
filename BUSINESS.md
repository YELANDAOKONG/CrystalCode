# Product Definition

## Mission

CrystalCode is a local coding agent for real repositories. A developer runs
it in a workspace, talks to a model, and lets the agent inspect and change
code under explicit Plan/Work modes and a risk-aware approval policy.

It is a product, not a library demo. Crystal supplies protocol types and tool
dispatch. This repository supplies the coding experience: prompts, tools,
approval, compaction, persistence, providers, and the terminal UI.

## Intended users

Developers who want a Claude Code / Codex-class coding loop on .NET, backed by
the Crystal library they already own.

## Current product

The current product is a terminal application. `crystal` opens the
terminal, which is the only operator surface. `crystal space` opens that
terminal in the operator space at `~/.crystal/space`. `crystal run` is a
headless entry for one task and has no operator. `crystal version` prints
the build identity and exits. The engine behind the terminal and `crystal run` is a
separate library with no terminal code. The product can:

- stream a model turn with tool calls, and queue follow-ups while it runs;
- configure per-turn model-call, tool-call, and wall-clock budgets, including
  unlimited budgets;
- configure the built-in bash per-command timeout, including unlimited;
- retry a failed model round on rate limits, server errors, timeouts,
  network faults, and incomplete streams, waiting with backoff, a
  countdown on the progress row, and a transcript note;
- switch the configured provider and model from `/model` without restarting;
- switch Plan (built-in reads; no edit, write, or bash) and Work (edit,
  write, shell); operator tool sets may add extra catalog entries to
  either;
- approve side effects manually, by a reviewing model, or by full
  pass-through according to risk and authority;
- ask one or more operator questions in one request, with described choices,
  single or multiple selection, optional custom answers, and confirmation;
- compact the context sent to the model when usage approaches the model
  window, or when the operator runs `/compact`, while keeping the full
  conversation for resume, fork, and export;
- ask before the first interactive session in a directory, when workspace
  trust is on (the default). Trust is the git root when the workspace is
  inside a repository, and the workspace itself otherwise. A yes is
  remembered under `~/.crystal/trusted.json`. A no exits without recording
  a denial. `/cd` into an untrusted root asks again and stays put on no.
  `/trust` shows the switch, turns it on or off, or forgets the current
  root. Turning the switch off skips later prompts and keeps the ledger.
  The operator space at `~/.crystal/space` is trusted without a prompt and
  without a ledger entry. Trust stops at that directory and does not climb
  to a parent git repository. `/trust forget` leaves it trusted. A
  subdirectory follows the ordinary rule unless `space` itself is the git
  root. `crystal space` creates the directory when it is missing;
- run one task without a terminal through `crystal run`, then exit.
  Flags on that command override provider, model, workspace, home,
  approval, the approval model, Plan or Work, thinking, prompt set, skills,
  external tools, and turn quotas for that process only. They are not written
  to `config.json`. Stdout is a readable plain-text trace, or one JSON object
  per line when `--format json` is set. Review and Audit still use the
  reviewing model. That model can be a separate provider and model, and
  the choice can be turned off.
  Anything that would ask the operator is denied. Directory trust for that
  command is its own switch, `--workspace-trust on|off`. Omitting it checks
  trust even when the interactive switch is off. `off` skips the check for
  that process and does not record the directory;
- persist configuration, permissions, and sessions under `~/.crystal`;
- recall submitted prompts from the composer with Up/Down and retain up to 200
  recent text-only entries across runs for the same workspace;
- select a reusable Home prompt set without changing the higher-priority direct
  prompt overrides in Home or the workspace;
- list saved sessions for the current workspace or every workspace; resume the
  current workspace, a named directory, or every workspace, entering that
  directory when the choice names one; and fork into a new independent session;
- discover OpenCode-compatible agent skills and load them through the
  `skill` tool when Skills is enabled;
- discover operator tool sets under `~/.crystal/tools` and
  `<workspace>/.crystal/tools` and register them as extra catalog tools
  when External Tools is enabled;
- load operator plugins from `~/.crystal/plugins` and
  `<workspace>/.crystal/plugins` when Plugins is enabled, including
  tools, protocol clients, classifiers, slash commands, hooks, and raw
  hooks. Hooks may revise a user message before it is stored and may
  revise the text of the outbound model request without changing the saved
  archive. A raw hook is a privileged extension point. It may rebuild one
  outbound request, replace composed prompt and compaction text with any
  string, and replace an approval classification with any risk, authority,
  summary, and prompt requirement. The session names each plugin that
  registers one. Ordinary hooks stay append-only for those prompts and
  cannot lower approval risk. One plugin or one external tool set can be
  enabled or disabled by directory name from the session or from
  `crystal plugins` and `crystal tools`. Those commands write that
  manifest's `enabled` field and do not write `config.json`. Their default
  output is a Spectre.Console table; `--format text` prints aligned plain
  text;
- honor an author-declared `approval: always` in a tool set for ordinary
  workspace-bounded calls, with the operator choosing per source whether
  declarations take effect through `externalToolApproval` in
  `config.json` (Home and Project each `author` or `host`; defaults are
  Home `author`, Project `host`);
- use DeepSeek and OpenAI-compatible Chat Completions, OpenAI Responses,
  Anthropic Messages, native Gemini GenerateContent, and native Ollama Chat
  adapters, including user-added gateways;
- group one gateway's protocol endpoints under a single provider name and route
  each uniquely named model through its configured protocol;
- attach PNG, JPEG, GIF, or WebP images from the workspace, paste clipboard
  images on supported terminals, copy submitted images into Home as validated
  binary files, persist references with sessions, export available images inline
  in JSON, and
  carry generic plugin-returned images into the next model round when a model
  explicitly declares image-input support;
- color pasted image markers and edit them as single composer units while
  identically spelled user text remains ordinary text; discard unsent images
  whose attached markers were deleted;
- register built-in tools and providers through an in-process plugin table.

## Deferred product work

The following capabilities are part of the product direction but are not yet
implemented in the current build:

- parent/child Agents through `Crystal.Harness.AgentHarness`;
- MCP servers;
- an operating-system sandbox;
- provider protocols other than DeepSeek and OpenAI-compatible Chat
  Completions, OpenAI Responses, Anthropic Messages, Gemini GenerateContent,
  and Ollama Chat;
- audio and video input, and image, audio, or video model output;
- editing provider definitions in the TUI;
- a module system below plugins and raw hooks, reached through reflection
  or another mechanism, once its behavior and ownership are defined;

TODO: add audio/video input and non-text model output only after their terminal
interaction, persistence, size, and provider semantics are defined. TODO: add
MCP without bypassing the existing plugin, approval, and media boundaries.

They were deferred until the product had a concrete use case and enough runtime
infrastructure to support them safely; they are not excluded from the product.
Implement each capability only when its behavior and ownership are defined, and
do not reserve empty public types in advance.

More hook methods will be added later on the existing hook interfaces, and
are not reserved as empty types in advance. Whether plugins declare
dependencies on one another, and related loading questions, are under
consideration and are not decided. They are not part of the deferred list
above until a decision is made.

## Relationship to Crystal

Crystal is provider-neutral, prompt-neutral, and tool-neutral. It does not
select a model, write a system prompt, ship a filesystem tool, compress
context, or draw a UI.

Crystal.Harness is a named-Agent composition runtime with shared budgets. It
is not this product. This product is named CrystalCode because it is the
coding product built on Crystal. Its Chinese name is "晶码".

## Data

User data lives in `~/.crystal`. Prompts may be replaced in
`~/.crystal/prompts` and the project's `.crystal/prompts`. Provider and model
definitions live in `~/.crystal/providers.json`; changing preferences in
`config.json` does not rewrite those definitions. The legacy
`config.json.providers` field remains readable for existing installations.
Home-only reusable prompt sets live under `~/.crystal/promptsets`; workspace
hints remain independent and are appended from `instructions.md`, `.crystal.md`, and
OpenCode-compatible `AGENTS.md` / `CLAUDE.md` files. Those rule files
are never prompt overlays. Skills are discovered from Crystal,
OpenCode, Claude, and Agents skill directories and loaded through the
`skill` tool when enabled. Operator tool sets live under `tools/` in
the home and project `.crystal` trees and are loaded as extra `ITool`
or `IMultimodalTool` entries when External Tools is enabled. A dotnet
tool may also implement `CrystalCode.Tools.IHostTool` or
`IHostMultimodalTool` and then receives the workspace root, session id,
and approval mode on each call. Tools that implement only Crystal's
interfaces stay unchanged. Native
multimodal entries are exposed only while the selected model and provider
support image input. Whether each source's author
approval declarations take effect is stored under `externalToolApproval`
in `config.json`. The application never writes
secrets into the workspace.

## Runtime language

All runtime text is English: UI, logs, exceptions, tool outputs authored by
this product, and approval copy.
