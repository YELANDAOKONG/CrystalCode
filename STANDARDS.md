# Engineering Standards

## General rules

- Prefer a small explicit surface over convenience APIs with hidden behavior.
- Keep BUSINESS.md, ARCHITECTURE.md, and this file synchronized with material
  behavior changes.
- Runtime and exception text is plain English and contains no emoji.
- Model-visible tool text uses explicit LF separators
  (`ToolOutputText.LineSeparator`, `ToolOutputText.AppendLine`), never
  `Environment.NewLine` or `StringBuilder.AppendLine`. Tool text reaches
  providers and session files, so it stays identical on every platform.
- Crystal-authored diagnostics rules still apply to host logs: no credentials,
  no raw API keys, no secret file contents.
- Comments explain constraints and intent rather than syntax.
- Do not leave commented-out production code.
- Do not modify dependencies without explicit user authorization.

## Source organization

- File-scoped namespaces.
- Exactly one top-level type per file. File name equals type name.
- Folder path under the project root equals the namespace after the project
  name.
- Using directives: System, then third-party, then Crystal, then
  CrystalCode, with a blank line between groups that are present.
- No top-level statements.
- Solution Explorer layout follows Visual Studio / Rider: `.sln` at the
  repository root, project folders beside it, tests as
  `{Project}.Tests` siblings (`CrystalCode.Tests`,
  `CrystalCode.Engine.Tests`, `CrystalCode.Display.Tests`,
  `CrystalCode.Providers.Tests`). No `src/` or root `tests/` tree.
- Engine code never calls `Console`, never draws, and never reads keys.
  Report through a `SessionEvent` or a `SessionFrontEnd` contract. New
  events are sealed immutable records, one per file under
  `CrystalCode.Engine/Events`, and carry engine values rather than display
  captions.
- Do not widen `InternalsVisibleTo` to give a front end engine internals.
  Make the member public when a second front end could need it, otherwise
  keep it out of the front end.
- Events carry plain data that a front end may serialize. Do not put
  settings, provider definitions, live catalogs, or plugin objects on an
  event; `SessionEventDataTests` enforces this.
- Front ends prepare sessions through `CrystalCode.Engine.Launch` and do not
  copy override, trust, or resume logic.

## C# conventions

- PascalCase for types and public members, camelCase for locals and
  parameters, `_camelCase` for private fields.
- Prefix interfaces with `I`.
- Use C# keywords (`string`, `int`) instead of CLR type names.
- Braces on every control-flow block.
- Every `switch` has an explicit fallback.
- Immutable records for value data. Classes for stateful behavior.
- Collection expressions when they improve clarity.
- `nameof` for parameter and code-element references.
- Avoid magic numbers, double negatives, nested loops, and clever compression.
- Nullable reference types stay enabled.
- Declare variables near first use, one variable per declaration.

## Async

- External or I/O operations are asynchronous.
- Async methods end in `Async`.
- `CancellationToken` is the last parameter and is propagated.
- Streams return `IAsyncEnumerable<T>`.
- Never call `Result`, `Wait`, or `GetAwaiter().GetResult()`.
- Library-style code in Providers uses `ConfigureAwait(false)`.
- Application code in the executable host and in CrystalCode.Engine does not
  need `ConfigureAwait`. A front end awaits engine calls and never blocks on
  them.
- Do not create unobserved background work.

## Safety

- Outside Full mode, write, edit, read, list, tree, glob, grep, and search of paths outside the
  workspace require approval unless session directory access covers the action.
  Session `read` permits file reads; `write` includes reads and retains normal
  action approval. Explicit read-only writes are rejected before grants, raw
  approval advice, or review and checked again by the built-in write/edit path
  resolver. Rules are saved with the session, not `config.json` or
  `permissions.json`. Resume restores those saved rules and does not reapply
  defaults. A new session, clear, fork, and a successful workspace change seed
  `~/.crystal/access.json` for that canonical workspace root unless
  `--allow-read` or `--allow-write` was passed; those flags replace the defaults
  for that process and are not written back. A refused or no-op workspace
  change keeps the current rules.
  Canonical directory boundaries and most-specific matching include the implicit
  writable workspace. Persist the lexical absolute entry path when it differs
  from the canonical target, for revocation after symlink deletion or retargeting;
  never use that alias for authorization or re-resolve it on resume. Validate saved
  access before returning a resumable document or allocating a session. Invalid
  access must produce a visible error, not crash interactive startup or replace
  the current session. Search descriptions and path schemas must agree that roots
  may be workspace-relative or absolute and only the requested root is searched.
  Unlisted outside paths retain approval: in Review or Audit
  the reviewing model judges them; otherwise the operator is asked. Edit, Review,
  and Audit do not auto-pass unlisted outside writes or edits. Full auto-passes
  all calls regardless of risk, authority, or forced-prompt flags and bypasses
  session read-only rules and execution path fences. Credential paths remain
  prohibited. Tool catalogs, validation, OS permissions, cancellation, and budgets
  still apply. These rules do not
  sandbox shell, background shell, plugins, or external processes.
  External dotnet tools may supply a
  side-effect-free pre-classifier when their actual write paths are embedded
  in arguments; it must fail closed, it can only raise risk or authority for
  the call, and it never replaces the Write + Workspace floor, the declared
  path checks, or the source trust policy. Its asynchronous entry receives
  cancellation and a separate two-second budget. Timeout/cancellation disables
  the classifier until reload; late tasks stay observed. Invalid fields,
  exceptions, and timeouts fail the proposal closed on both classification
  axes. Final summaries are host-authored. Reusable grants only apply to
  Workspace Read/Write without a forced prompt; forced prompts also prevent
  reviewer Allow from executing without the operator. Path checks follow
  symbolic links to their final target. A link that leaves the workspace is outside
  the workspace.
- When Skills is enabled, any path
  inside a Skills search directory (`skill` / `skills` trees)
  auto-passes as a workspace read. Credential paths stay Forbidden,
  including workspace-relative `.ssh`, `.gnupg`, and
  `.crystal/credentials.json`. Classification and execution both reject
  them, and workspace-wide search skips those files.
- Shell classification treats `sudo`, destructive filesystem commands,
  pipe-to-shell downloads, force-push, and credential-path writes as
  Forbidden or Privileged. Outside Full mode, Forbidden never fully auto-passes. Review
  and Audit may deny those calls or escalate them to the
  operator.
- `crystal run` has no operator. Review and Audit still ask the reviewing
  model. Any call that would ask the operator is denied with the existing
  rejection text. The `question` tool is dismissed. Credential paths stay
  Forbidden. Flags on that command are not written to `config.json`. Do not
  put secrets on the command line.
- Credential files are written with owner-only access where the OS allows it.
- New provider catalog files are created with owner-only access because
  provider definitions may contain credential references or inline keys.
- Do not log request bodies that may contain secrets.
- Do not put image bytes, data URIs, or remote image URIs in composer text,
  transcript rendering, logs, diagnostics, or exception messages. Use stable
  `[Image #N]` markers.
- Treat attachment provenance as separate from marker spelling. User-entered
  text that resembles an image marker is plain text, not a media reference.
- Prompt history persists text-only entries under `~/.crystal` with owner-only
  permissions where supported. Do not persist image markers without their
  session attachment identity.
- Validate local image content by signature, enforce the host size limit, and
  preserve exact bytes and MIME type. Do not decode, transform, fetch, or
  execute media implicitly.
- Validate size and MIME type again before copying session images to the
  owner-only media store. Persist and verify a content hash; write the media
  file before its session reference. Missing or damaged media must not make
  the remaining session unreadable.
- Keep text and image-capable turn budgets on the same configured limits.
  Parent and child calls reserve one shared budget atomically; retries do not
  acquire another logical model-call reservation. Only adjacent child task
  groups overlap. Preserve ordinary-tool barriers and result order. Child
  execution must use approved executors, with a filtered catalog beneath hooks.
  Child ledger updates and parent snapshots must be serialized, and restore
  must never sum saved child usage into an already-inclusive parent total.
  Context estimates must not be persisted as provider token usage.
  A null limit is unlimited; omitted budget fields retain the finite defaults.
- Built-in bash uses the same rule for `bashTimeoutSeconds`: omit it to keep
  600 seconds; `null` or `"unlimited"` removes the per-command timer.
- Background Bash uses the independent `backgroundBashTimeoutSeconds` timer:
  omit it to keep 10800 seconds (3 hours); `null` or `"unlimited"` removes that timer.
- Background Bash tasks are session-local and must remain observable through
  their status or bounded logs. They are not persisted, and session shutdown
  stops their process trees. A model must not create an unobserved background
  task.
- Extension runtime data lives under `data/{plugins,tools}/<directory>/` in
  the home and project `.crystal` trees. The host creates it when the
  extension first receives it and it is separate from the install tree. It is
  not a secret store: do not persist credentials there, and the project
  directory is workspace content.

## Dependencies

Authorized packages today:

- Spectre.Console in CrystalCode.Display (session rasterization) and
  CrystalCode (host cards that still build Spectre widgets). Never in
  CrystalCode.Engine.
- Spectre.Console.Cli in CrystalCode.
- Terminal.Gui in CrystalCode.Display only, with a floating version
  (`*`). It is parked for supply-chain review. Production and test code
  must not import `Terminal.Gui` types. Do not add a dummy `using` to
  silence unused-package warnings; document a restore suppression if
  NU1510 is raised.
- Newtonsoft.Json, Newtonsoft.Json.Bson, and System.Text.Json in
  CrystalCode.Engine and CrystalCode.Providers, where the Crystal sibling
  already requires them for project-reference consistency.
- ModelContextProtocol.Core in CrystalCode.Engine only, pinned to 2.3.0, as
  the MCP client. Its transitive Microsoft.Extensions.AI.Abstractions and
  Microsoft.Extensions.Logging.Abstractions packages come with it. The
  Engine code that uses it stays in `CrystalCode.Engine/Mcp`; no front end or
  contract assembly references it.
- xUnit and Microsoft.NET.Test.Sdk in CrystalCode.Tests,
  CrystalCode.Engine.Tests, CrystalCode.Display.Tests, and
  CrystalCode.Providers.Tests, CrystalCode.Server.Tests, and
  CrystalCode.Bridge.Tests, at the same versions in every test project.
- The `Microsoft.AspNetCore.App` shared framework (`FrameworkReference`) in
  CrystalCode.Server only, and Microsoft-published ASP.NET Core packages that
  server needs. The operator approved Microsoft packages for this project.
  Never in CrystalCode.Engine or CrystalCode.Display.
- No package in CrystalCode.Bridge. JSON-RPC and the ACP wire use
  `System.Text.Json` from the shared framework; no ACP SDK or JSON-RPC
  library is referenced.

Do not add another package without asking.

## Verification

```bash
dotnet build CrystalCode.sln
dotnet test CrystalCode.sln
```

Tests run on Windows, macOS, and Linux (the release workflow runs the
suite on each), so they stay portable. Do not hardcode a single-platform
path such as `/tmp` or a Windows drive root in a test that touches the
real filesystem; derive it from `Path.GetTempPath()`, `Path.Combine`, or an
existing temporary-directory helper. A test that only compares path strings
and never reads or writes the disk is not constrained.

Do not claim coverage a test project does not actually exercise. Session,
approval, compaction, storage, tool, launch, and session runner behavior is
tested in CrystalCode.Engine.Tests, including a headless session driven by a scripted
model through `ISessionObserver`. Terminal projection, renderer, and prompt
surfaces are tested in CrystalCode.Tests. `crystal run` is tested there
with a scripted model for a completed reply, a review allow that executes
a tool, an operator prompt that is denied with a non-zero exit, and a
failed request. Its JSON events, including child events and usage totals,
are tested against the observer directly. `crystal server` is tested in CrystalCode.Server.Tests
against a real Kestrel listener on a loopback port chosen by the OS and a
scripted model: turns, events, reconnect, targeted replies, every interaction
kind, recall, quick prompts, images, export, resume, shutdown, scopes,
tickets, Host and Origin checks, the parity guards, and the secret-leak test.
`crystal bridge` is tested in CrystalCode.Bridge.Tests over in-process pipes
with a scripted model: initialize, new, load with replay, resume without
replay, load into another `cwd`, list paging, prompts with every content
block and file image links, streaming, cancel during streaming, a follow-up,
and a pending permission, `$/cancel_request`, close, `/clear` keeping the ACP
id, `/cd`, `/quit`, permission answers, diffs, plans, config options and
modes with next-turn settings and refusal while busy, available commands, usage, session info,
reports, the question, trust, and resume forms with and without form
support, editor MCP servers without leaking their values, additional roots,
error codes, launch and trust refusals, stdin closing, protocol-only stdout,
compaction with and without `session.compaction`, fork, children and child
approvals with and without the subagents opt-in, the `_crystal/*` methods,
no `_crystal/*` notification without the opt-in,
the parity guards, the secret-leak test, and the dependency guard.
`ToolFileChanges`, session MCP servers, additional roots,
`TryAttachImageFile`, the option catalog, `HelpText` and `StatsText`, the
compaction events, and the fork launch are tested in
CrystalCode.Engine.Tests.
Token resolution, `crystal server --help`, `crystal bridge --help`, and
Bridge console rebinding are tested in CrystalCode.Tests. Frame,
composer, and paint behavior is tested in CrystalCode.Display.Tests.
Adapters are tested in CrystalCode.Providers.Tests.
`EngineAssemblyTests` and `DisplayAssemblyTests`
guard the dependency direction and must not be weakened to make a change pass.
