# Engineering Standards

## General rules

- Prefer a small explicit surface over convenience APIs with hidden behavior.
- Keep BUSINESS.md, ARCHITECTURE.md, and this file synchronized with material
  behavior changes.
- Runtime and exception text is plain English and contains no emoji.
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
  CrystalHarness, with a blank line between groups that are present.
- No top-level statements.
- Solution Explorer layout follows Visual Studio / Rider: `.sln` at the
  repository root, project folders beside it, tests as
  `{Project}.Tests` siblings (`CrystalHarness.Tests`,
  `CrystalHarness.Providers.Tests`). No `src/` or root `tests/` tree.

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
- Application code in the executable host does not need `ConfigureAwait`.
- Do not create unobserved background work.

## Safety

- Workspace tools reject paths that escape the workspace root.
- Shell classification treats `sudo`, destructive filesystem commands,
  pipe-to-shell downloads, force-push, and credential-path writes as
  Forbidden or Privileged. Forbidden never fully auto-passes. Review
  mode may deny those calls or escalate them to the operator.
- Credential files are written with owner-only access where the OS allows it.
- Do not log request bodies that may contain secrets.

## Dependencies

Authorized packages today:

- Spectre.Console and Spectre.Console.Cli in CrystalHarness.
- Newtonsoft.Json, Newtonsoft.Json.Bson, and System.Text.Json where the
  Crystal sibling already requires them for project-reference consistency.
- xUnit and Microsoft.NET.Test.Sdk in CrystalHarness.Tests and
  CrystalHarness.Providers.Tests.

Do not add another package without asking.

## Verification

```bash
dotnet build CrystalHarness.sln
dotnet test CrystalHarness.sln
```

Do not claim coverage a test project does not actually exercise. Host
behavior is tested in CrystalHarness.Tests. Adapters are tested in
CrystalHarness.Providers.Tests.
