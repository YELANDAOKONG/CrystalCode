# Agent Instructions

These instructions apply to the entire repository.

## Required reading

Read these documents before changing production code:

1. BUSINESS.md defines the product boundary and terminology.
2. ARCHITECTURE.md defines component ownership and runtime semantics.
3. STANDARDS.md defines coding, dependency, and verification rules.

When a design decision changes, update the relevant document in the same
change. Implementation must never become the only source of truth.

## Product

CrystalHarness is a production coding CLI. It is not a Crystal demo and not a
replacement for Crystal.

Crystal is a sibling library at `../Crystal`. Consume it. Do not modify it.

CrystalDebugger at `../CrystalDebugger` is a reference for tool and turn
semantics. Do not copy its Demo UI into this product.

## Non-negotiable invariants

- Do not change files under `../Crystal`.
- Do not add, remove, or update NuGet packages without explicit user approval.
- Runtime text is plain English. No emoji in exceptions, logs, or UI chrome.
- Secrets never appear in source, logs, diagnostics, or commit contents.
- Crystal remains prompt-neutral. Every model-bound string this product sends
  is authored here: system prompts, compaction summaries, rejection text, and
  tool exception mapping. Operators may replace Work, Plan, and Review via
  `~/.crystal/prompts` and `<workspace>/.crystal/prompts`. `AGENTS.md` and
  `CLAUDE.md` are OpenCode-compatible instructions that append; they do
  not replace those prompts. Do not invent additional prompt file names.
- Provider adapters implement only Crystal chat contracts. They do not own
  tools, prompts, UI, or `~/.crystal` layout.
- Public data values are immutable. One type per file. File-scoped namespaces.
- No top-level statements. Explicit `Program.Main`.

## Change rules

- Ask before adding a project, a public architectural boundary, or a new
  provider family. Extra tools and protocols go through `IPlugin` /
  `PluginRegistry`. Do not load assemblies from disk.
- Keep assembly ownership consistent with ARCHITECTURE.md.
- Preserve workspace fencing for every filesystem and shell tool.
- Approval decisions go through `Crystal.Tools.ToolInvocationPolicy`. Do not
  invoke side-effect tools by bypassing the executor.
- Context compaction is owned here. Crystal will not truncate or summarize.
- Do not perform repository history operations unless explicitly requested.

## Verification

While developing, run the narrowest relevant build. Before handoff:

```bash
dotnet build CrystalHarness.sln
dotnet test CrystalHarness.sln
```
