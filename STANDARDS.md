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

- Write, edit, read, list, glob, and grep of paths outside the workspace
  require approval. In Review or Audit the reviewing model judges them;
  otherwise the operator is asked. Edit, Review, and Full do not
  auto-pass an outside write or edit. Path checks follow symbolic links
  to their final target. A link that leaves the workspace is outside
  the workspace.
- When Skills is enabled, any path
  inside a Skills search directory (`skill` / `skills` trees)
  auto-passes as a workspace read. Credential paths stay Forbidden,
  including workspace-relative `.ssh`, `.gnupg`, and
  `.crystal/credentials.json`. Classification and execution both reject
  them, and workspace-wide search skips those files.
- Shell classification treats `sudo`, destructive filesystem commands,
  pipe-to-shell downloads, force-push, and credential-path writes as
  Forbidden or Privileged. Forbidden never fully auto-passes. Review
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
  A null limit is unlimited; omitted budget fields retain the finite defaults.
- Built-in bash uses the same rule for `bashTimeoutSeconds`: omit it to keep
  120 seconds; `null` or `"unlimited"` removes the per-command timer.

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
- xUnit and Microsoft.NET.Test.Sdk in CrystalCode.Tests,
  CrystalCode.Engine.Tests, CrystalCode.Display.Tests, and
  CrystalCode.Providers.Tests.

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
approval, compaction, storage, and tool behavior is tested in
CrystalCode.Engine.Tests, including a headless session driven by a scripted
model through `ISessionObserver`. Terminal projection, renderer, and prompt
surfaces are tested in CrystalCode.Tests. `crystal run` is tested there
with a scripted model for a completed reply, a review allow that executes
a tool, and an operator prompt that is denied with a non-zero exit. Frame,
composer, and paint behavior is tested in CrystalCode.Display.Tests.
Adapters are tested in CrystalCode.Providers.Tests.
`EngineAssemblyTests` and `DisplayAssemblyTests`
guard the dependency direction and must not be weakened to make a change pass.
