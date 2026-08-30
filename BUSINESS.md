# Product Definition

## Mission

CrystalHarness is a local coding agent for real repositories. A developer runs
it in a workspace, talks to a model, and lets the agent inspect and change
code under explicit Plan/Work modes and a risk-aware approval policy.

It is a product, not a library demo. Crystal supplies protocol types and tool
dispatch. This repository supplies the coding experience: prompts, tools,
approval, compaction, persistence, providers, and the terminal UI.

## Intended users

Developers who want a Claude Code / Codex-class coding loop on .NET, backed by
the Crystal library they already own.

## Current product

The current product is an interactive terminal application that can:

- stream a model turn with tool calls, and queue follow-ups while it runs;
- switch Plan (read-only) and Work (edit, write, shell);
- approve side effects manually, by a reviewing model, or by full
  pass-through according to risk and authority;
- compact conversation context when usage approaches the model window,
  or when the operator runs `/compact`;
- persist configuration, permissions, and sessions under `~/.crystal`;
- use DeepSeek and OpenAI-compatible chat adapters, including user-added
  compatible endpoints;
- register built-in tools and providers through an in-process plugin table.

## Current exclusions

The current product does not include:

- loading third-party plugin assemblies from disk;
- parent/child Agents through `Crystal.Harness.AgentHarness`;
- MCP servers;
- a headless CI runner;
- an operating-system sandbox;
- Chat Completions dialects other than DeepSeek and OpenAI-compatible;
- multimodal coding (images, audio, video).

Those remain later product work. Do not reserve empty public types for them.

## Relationship to Crystal

Crystal is provider-neutral, prompt-neutral, and tool-neutral. It does not
select a model, write a system prompt, ship a filesystem tool, compress
context, or draw a UI.

Crystal.Harness is a named-Agent composition runtime with shared budgets. It
is not this product. This product is named CrystalHarness because it is the
coding harness built on Crystal.

## Data

User data lives in `~/.crystal`. Prompts may be replaced in
`~/.crystal/prompts` and the project's `.crystal/prompts`. Workspace
hints are appended from `instructions.md`, `.crystal.md`, and
OpenCode-compatible `AGENTS.md` / `CLAUDE.md` files. Those rule files
are never prompt overlays. The application never writes secrets into
the workspace.

## Runtime language

All runtime text is English: UI, logs, exceptions, tool outputs authored by
this product, and approval copy.
