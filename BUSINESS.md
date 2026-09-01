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

The current product is a terminal UI application. The terminal is the only
operator surface and the only entry. It can:

- stream a model turn with tool calls, and queue follow-ups while it runs;
- retry a failed model round on rate limits, server errors, timeouts,
  network faults, and incomplete streams, waiting with backoff;
- switch the configured provider and model from `/model` without restarting;
- switch Plan (built-in reads; no edit, write, or bash) and Work (edit,
  write, shell); operator tool sets may add extra catalog entries to
  either;
- approve side effects manually, by a reviewing model, or by full
  pass-through according to risk and authority;
- compact conversation context when usage approaches the model window,
  or when the operator runs `/compact`;
- persist configuration, permissions, and sessions under `~/.crystal`;
- discover OpenCode-compatible agent skills and load them through the
  `skill` tool when Skills is enabled;
- discover operator tool sets under `~/.crystal/tools` and
  `<workspace>/.crystal/tools` and register them as extra catalog tools
  when External Tools is enabled;
- use DeepSeek and OpenAI-compatible Chat Completions, OpenAI Responses, and
  Anthropic Messages adapters, including user-added gateways;
- register built-in tools and providers through an in-process plugin table.

## Current exclusions

The current product does not include:

- loading `IPlugin` assemblies from `~/.crystal/plugins/`;
- parent/child Agents through `Crystal.Harness.AgentHarness`;
- MCP servers;
- a headless CI runner;
- an operating-system sandbox;
- provider protocols other than DeepSeek and OpenAI-compatible Chat
  Completions, OpenAI Responses, and Anthropic Messages;
- multimodal coding (images, audio, video).

Those remain later product work. Do not reserve empty public types for them.

## Relationship to Crystal

Crystal is provider-neutral, prompt-neutral, and tool-neutral. It does not
select a model, write a system prompt, ship a filesystem tool, compress
context, or draw a UI.

Crystal.Harness is a named-Agent composition runtime with shared budgets. It
is not this product. This product is named CrystalCode because it is the
coding product built on Crystal.

## Data

User data lives in `~/.crystal`. Prompts may be replaced in
`~/.crystal/prompts` and the project's `.crystal/prompts`. Workspace
hints are appended from `instructions.md`, `.crystal.md`, and
OpenCode-compatible `AGENTS.md` / `CLAUDE.md` files. Those rule files
are never prompt overlays. Skills are discovered from Crystal,
OpenCode, Claude, and Agents skill directories and loaded through the
`skill` tool when enabled. Operator tool sets live under `tools/` in
the home and project `.crystal` trees and are loaded as extra `ITool`
entries when External Tools is enabled. The application never writes
secrets into the workspace.

## Runtime language

All runtime text is English: UI, logs, exceptions, tool outputs authored by
this product, and approval copy.
