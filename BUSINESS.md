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
terminal, which is the only operator surface. `crystal run` is a headless
entry for one task and has no operator. The engine behind both is a
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
- compact conversation context when usage approaches the model window,
  or when the operator runs `/compact`;
- run one task without a terminal through `crystal run`, then exit.
  Flags on that command override provider, model, workspace, home,
  approval, Plan or Work, thinking, prompt set, skills, external tools,
  and turn quotas for that process only. They are not written to
  `config.json`. Stdout is plain text, or one JSON object per line when
  `--format json` is set. Review and Audit still use the reviewing model.
  Anything that would ask the operator is denied;
- persist configuration, permissions, and sessions under `~/.crystal`;
- recall submitted prompts from the composer with Up/Down and retain up to 200
  recent text-only entries across runs for the same workspace;
- select a reusable Home prompt set without changing the higher-priority direct
  prompt overrides in Home or the workspace;
- list saved sessions for the current workspace or every workspace, choose a
  saved conversation by recent update time, and fork into a new independent session;
- discover OpenCode-compatible agent skills and load them through the
  `skill` tool when Skills is enabled;
- discover operator tool sets under `~/.crystal/tools` and
  `<workspace>/.crystal/tools` and register them as extra catalog tools
  when External Tools is enabled;
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

- loading `IPlugin` assemblies from `~/.crystal/plugins/`;
- parent/child Agents through `Crystal.Harness.AgentHarness`;
- MCP servers;
- an operating-system sandbox;
- provider protocols other than DeepSeek and OpenAI-compatible Chat
  Completions, OpenAI Responses, Anthropic Messages, Gemini GenerateContent,
  and Ollama Chat;
- audio and video input, and image, audio, or video model output;

TODO: add audio/video input and non-text model output only after their terminal
interaction, persistence, size, and provider semantics are defined. TODO: add
MCP without bypassing the existing plugin, approval, and media boundaries.

They were deferred until the product had a concrete use case and enough runtime
infrastructure to support them safely; they are not excluded from the product.
Implement each capability only when its behavior and ownership are defined, and
do not reserve empty public types in advance.

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
