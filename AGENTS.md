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

CrystalCode is a production coding TUI. The terminal is the only
operator surface. `crystal run` is the headless entry for one task. It
is not an operator surface: anything that would ask the operator is
denied. Do not put secrets on its command line, and do not persist its
flags to `config.json`. It is not a Crystal demo and not a replacement
for Crystal.

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
- `CrystalCode.Engine` is front-end neutral. It must not reference
  Spectre.Console, Terminal.Gui, `CrystalCode.Display`, or the executable,
  and it must not touch the console. Everything a front end needs arrives as
  a `SessionEvent` or flows through `SessionFrontEnd`. Terminal behavior
  belongs in `CrystalCode`; frame and composer behavior in
  `CrystalCode.Display`. Do not fork engine logic into a front end.
  If a later desktop front end uses Avalonia, write its UI in C# markup.
  Do not add XAML or AXAML files.
- Public data values are immutable. One type per file. File-scoped namespaces.
- No top-level statements. Explicit `Program.Main`.

## Change rules

- Ask before adding a project, a public architectural boundary, or a new
  provider family. Extra first-party tools and protocols go through
  `IPlugin` / `PluginRegistry`. Operator tools go through tool sets under
  `~/.crystal/tools` and `<workspace>/.crystal/tools` (`ExternalCatalog`).
  Do not load `IPlugin` assemblies from disk. Dotnet tool sets load class
  libraries from the set directory only.
- Keep assembly ownership consistent with ARCHITECTURE.md.
- Write, edit, read, glob, and grep of paths outside the workspace
  require approval (the Review model in Review or Audit, otherwise the
  operator). Edit, Review, and Full do not auto-pass an outside write
  or edit. When Skills
  is enabled, any path inside a Skills search directory (`skill` /
  `skills` trees) auto-passes. Credential paths stay Forbidden.
- Approval decisions go through `Crystal.Tools.ToolInvocationPolicy`. Do not
  invoke side-effect tools by bypassing the executor.
- Context compaction is owned here. Crystal will not truncate or summarize.
- Do not perform repository history operations unless explicitly requested.

## Verification

While developing, run the narrowest relevant build. Before handoff:

```bash
dotnet build CrystalCode.sln
dotnet test CrystalCode.sln
```
