# Agent Instructions

These instructions apply to the entire repository.

## Required reading

Read these documents before changing production code:

1. BUSINESS.md defines the product boundary and terminology.
2. ARCHITECTURE.md defines component ownership and runtime semantics.
3. STANDARDS.md defines coding, dependency, and verification rules.
4. TODO.md buckets deferred, priority, and undecided work. It does not change
   product scope. BUSINESS.md remains authoritative.

When a design decision changes, update the relevant document in the same
change. Implementation must never become the only source of truth. A move
between TODO.md buckets is a design decision.

## Product

CrystalCode is a production coding TUI. The terminal is the only
operator surface in this build. `crystal server` is the defined second
operator surface (see [docs/server.md](docs/server.md)); it is not in this
build yet. `crystal run` is the headless entry for one task. It
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
  tool exception mapping. Operators may replace Work, Plan, Review, the Review
  user turn, extended approval system and user text, topic naming, compaction,
  and image description via
  `~/.crystal/prompts`, a selected prompt set, and `<workspace>/.crystal/prompts`.
  The file stems are `work`, `plan`, `review`, `review.user`,
  `review.extended`, `review.extended.user`, `topic`,
  `compaction.system`, `compaction.user`, `image.system`, and `image.user`.
  Built-in child Agents add `agents/general`, `agents/work`, and `agents/plan`
  under those same prompt, prompt-set, and prompt-attachment directories.
  `AGENTS.md` and
  `CLAUDE.md` are OpenCode-compatible instructions that append; they do
  not replace those prompts. Do not invent prompt file names beyond that list.
  A custom mode's `modes/<name>/prompt.md` is that mode's own body, not a
  named prompt overlay, and prompt sets, overrides, and attachments never edit it.
- Provider adapters implement only Crystal chat contracts. They do not own
  tools, prompts, UI, or `~/.crystal` layout.
- `CrystalCode.Engine` is front-end neutral. It must not reference
  Spectre.Console, Terminal.Gui, `CrystalCode.Display`, or the executable,
  and it must not touch the console. Everything a front end needs arrives as
  a `SessionEvent` or flows through `SessionFrontEnd`. Terminal behavior
  belongs in `CrystalCode`; frame and composer behavior in
  `CrystalCode.Display`. Do not fork engine logic into a front end.
  If a later desktop front end uses Avalonia, write its UI in C# markup.
  Do not add XAML or AXAML files. Launch logic lives in
  `CrystalCode.Engine.Launch`; a front end maps its own command line onto
  `SessionLaunchOptions` and does not copy override, trust, or resume code.
- `CrystalCode.Server`, when added, is a front end like the terminal. It
  holds no product logic, does not reference `CrystalCode.Display`,
  Spectre.Console, Terminal.Gui, or the executable, and serializes only its
  own wire DTOs. Its protocol carries no version identifier. One process
  hosts one active session.
- Public data values are immutable. One type per file. File-scoped namespaces.
- No top-level statements. Explicit `Program.Main`.

## Change rules

- Ask before adding a project, a public architectural boundary, or a new
  provider family. `CrystalCode.Server` and `CrystalCode.Server.Tests` are
  approved projects with the ownership in ARCHITECTURE.md.
  `CrystalCode.Platform` is deferred; do not add that
  project. Extra first-party tools and protocols go through
  `IPlugin` / `PluginRegistry`. Operator plugins load from `plugins/`
  through `CrystalCode.Plugins` (`PluginCatalog`), one load context per
  plugin. Do not load `CrystalCode.Engine` into that context. Operator
  tools go through tool sets under `~/.crystal/tools` and
  `<workspace>/.crystal/tools` (`ExternalCatalog`). Dotnet tool sets load
  class libraries from the set directory only.
- Keep assembly ownership consistent with ARCHITECTURE.md.
- Write, edit, read, list, tree, glob, grep, and search of paths outside the workspace
  includes session-authorized directories. Outside Full mode, explicit read-only
  writes are rejected before grants or review, and unlisted outside paths require
  approval (the Review model in Review or Audit, otherwise the operator).
  Full auto-passes all tool calls and bypasses directory access restrictions,
  including outside paths and session read-only directories. Credential paths
  remain prohibited. When Skills is enabled, any path inside a Skills search
  directory (`skill` / `skills` trees) auto-passes. These rules are not a shell
  or extension sandbox.
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
