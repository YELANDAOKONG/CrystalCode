---
name: csharp-code-organization
description: >
  Organize C# / .NET solutions, projects, folders, files, and namespaces.
  Use whenever creating, moving, renaming, scaffolding, reviewing, or generating
  .NET code — Visual Studio/Rider layouts, .csproj placement, test pairing,
  namespace wording, folder taxonomy, and Solution Explorer hygiene. Complements
  C# Clean Code (in-file style). Trigger even if the user does not say
  "structure", "organization", or "clean code". Never invent src/ or tests/
  roots, compound or acronym namespaces (ShopSystem, MyApp, AI), or an .Api
  host. Ask the user when the root word, project split, or file placement is
  uncertain — naming and layout are maintainability, not decoration.
license: MIT
---

# C# Code Organization

This skill constrains **where types live and what they are called** in any
.NET project: class libraries, consoles, workers, WPF, MAUI, ASP.NET, test
projects. It does not own statement-level style. Pair it with the
sibling C# Clean Code skill for file-scoped namespaces, braces, async,
and one-type-per-file at the syntax level.

Solution Explorer is the first reading surface. Names and folders are the
map of the system. Inventing a clever layout without asking produces code
people cannot scan.

## 1. Confirm the words before any file exists

Do not scaffold until the root identifier is settled.

**Root namespace = one English word.** It is the domain concept, spoken
aloud, not a ticker, not two words welded together, not an abbreviation.

```text
Bad as an address          Prefer
----------------------     -----------
AI                         Intelligence
NLP                        Language
ML                         Learning
DB / DAL                   Data
ShopSystem                 Shop
AcmeSystem                 Acme
MyApp                      the real product word
Auth / Id                  Identity
```

`AI.*` is an industry nickname. `Intelligence.*` is the thing itself.
The same test applies to every system: the namespace should read as a
word someone would use in a sentence.

**Namespaces are addresses. Type names are occupants.** Types may be
compound and may carry a role suffix (`CompletionService`,
`OrderController`). Namespace segments may not.

Forbidden in any namespace segment (which means: forbidden as project
names and as folders that become namespaces):

- Concatenated products: `ShopSystem`, `AcmeSystem`, `MyApp`
- Concatenated roles: `DataAccess`, `UserManagement`, `OrderProcessing`
- Acronyms and clipped jargon: `AI`, `NLP`, `DAL`, `Infra`, `Svc`, `Ctrl`
- Decorative layers added for uniqueness: `Company.Product.Core`

One-word plurals used as real categories are fine: `Interfaces`,
`Entities`, `Controllers`, `Orders`, `Tests`, `Hosting`.

If a platform idiom is itself glued (`ViewModels`, `ValueObjects`),
**ask** rather than creating that namespace silently.

**Applications are not libraries.** In ordinary non-NuGet systems there
is no collision to prevent. Do not prefix `Company.` or invent
`Product.Layer` solely so the name is "globally unique". Uniqueness
matters when publishing a public package; it is noise inside an app.
Default shape is one short word at the root, plus `Tests` when a second
assembly is real.

If the user has not given a single real word — they said "an AI system",
"a shop", "MyApp" — stop and ask what the root word is. Offer a concrete
candidate (`Intelligence`, `Shop`) and wait. Do not baptize the repo
yourself.

## 2. Disk layout follows Visual Studio / Rider, not Node

C# is not TypeScript. Do not wrap code in `src/`, `lib/`, `app/`, or a
root `tests/` package folder. Those are frontend and some `dotnet new` /
Aspire defaults. Flatten them when you create or when you add files.

```text
Intelligence.sln
Intelligence/
  Intelligence.csproj
  Interfaces/ICompletion.cs
  CompletionService.cs
Intelligence.Tests/
  Intelligence.Tests.csproj
  CompletionServiceTests.cs
```

Rules:

- `.sln` sits at the repository root, sibling to project folders.
- A project folder, its `.csproj`, its assembly name, and its root
  namespace are the **same identifier**.
- Tests are a **sibling project** named `{Project}.Tests`. Never
  `{Project}/Tests`, never `tests/{Project}.Tests`, never
  `{Product}.Tests.{Layer}`.
- Extra projects exist only for a real assembly boundary, and the extra
  segment is still one word (`Intelligence.Tests`, and if the user
  accepted it, `Shop.Controllers`).

Allowed at the solution root besides projects: `.editorconfig`,
`.gitignore`, `Directory.Build.props`, `Directory.Packages.props`,
`global.json`, `nuget.config`, `README.md`, `LICENSE`, and optional
non-code trees (`docs/`, `scripts/`, `.github/`). No loose business
`.cs` files at the solution root.

Do not copy ASP.NET into a console or a library. `Controllers` exist
only when there are controllers. There is no default `Api` project and
no default `Core` / `Infrastructure` split. Start with one project plus
`*.Tests`. Split when the user asks or when a boundary is already
obvious and confirmed.

## 3. Path, namespace, and file name are the same fact

File-scoped namespace. Folder path under the project root equals the
namespace after the project name.

```csharp
// Intelligence/Interfaces/ICompletion.cs
namespace Intelligence.Interfaces;

public interface ICompletion
{
}
```

```csharp
// Intelligence/CompletionService.cs
namespace Intelligence;

public sealed class CompletionService
{
}
```

Match spelling. Do not skip a folder in the namespace, and do not add a
segment the disk does not have.

Each file holds **exactly one top-level type** (class, record, struct,
interface, or enum). File name equals type name: `ICompletion.cs`,
`ICompletionService.cs`, `CompletionService.cs`. Nested private types
may stay with their owner. A second top-level type is a new file — never
an `enum` parked under a class "for convenience".

## 4. Classify; do not pile

A flat dump hides contracts in a list of files. Folders exist so the
next reader sees **kinds** in a glance.

```text
# Unreadable pile
IExample.cs
IExampleService.cs
ExampleDataService.cs

# Classified (illustrative — not a universal template)
Interfaces/IExample.cs
Interfaces/IExampleService.cs
ExampleDataService.cs
```

How to decide, case by case:

- Several files of the same kind, or a kind the reader must spot first
  (contracts, exceptions) → give them a folder.
- A single implementation next to its contracts may stay where the
  feature lives. Do not create `Services/` for one file just for
  symmetry.
- Folder names are one English word, PascalCase, no pinyin, no
  snake_case. Technical groups are usually plural (`Interfaces`,
  `Exceptions`). Feature groups are the domain noun (`Orders`).
- Do not pre-lay Entities / ValueObjects / Middlewares / Helpers because
  a blog showed that tree. Empty taxonomy is clutter.
- Never open junk drawers: `Utils/`, `Helpers/`, `Common/` as a dump,
  `Misc/`, `Temp/`. `Common/` is acceptable only for true primitives
  (`Result`, `Error`) and must be emptied as soon as a real name exists.

This mapping is an example, not a skeleton to generate every time. A WPF
project may want `Views/` and `Models/`. A library may only need
`Interfaces/`. A worker may only need `Program.cs` and a few types.
**If several placements are reasonable, ask.**

ASP.NET-specific: do not name a project or namespace `Api`. The native
unit is `Controller`. Prefer `Controllers` as the project or folder. If
the project is already `Shop.Controllers`, put `OrdersController.cs`
there — do not nest `Controllers/Controllers/`. `Program.cs`,
middleware, and options are host concerns; if their home is ambiguous,
ask whether they stay in the controllers project or in a one-word host
project named after the product.

## 5. Tests mirror production

```text
Intelligence/CompletionService.cs
Intelligence.Tests/CompletionServiceTests.cs

Intelligence/Interfaces/ICompletion.cs
Intelligence.Tests/          # do not add empty tests for raw interfaces
```

- Project: `{SutProject}.Tests`
- File: `{SutType}Tests.cs`
- Folders follow the production tree; skip generated noise (`Migrations`)
- Method names: `{Method}_{Scenario}_{Expected}`
- `InternalsVisibleTo` belongs on the production csproj when internals
  must be tested

## 6. Placement — decide, or ask

Before writing a `.cs` file, pick project, then folder, then file name,
then type. Never drop code into whichever directory is open.

| Creating | Default home |
| :--- | :--- |
| Root identifier still unknown | Ask. Do not create the repo. |
| Domain contract (`I…`) | `{Product}/Interfaces/` when more than one, or as asked |
| Implementation | Beside the feature, not inside `Interfaces/` |
| Extra assembly actually needed | Ask. Sibling project, one-word extra segment |
| Test | `{Product}.Tests/`, mirrored path |
| ASP.NET controller | `Controllers` project or folder — never `Api` |
| Exception | `Exceptions/` when there are several |
| Options (`IOptions<T>`) | `Options/` in the host that binds them |

One use case that needs several files: list the paths, then write. Keep
that use case together if the user is grouping by feature; keep kinds
together if they are grouping by kind. When you cannot tell which axis
they want, **ask**.

## 7. Agent workflow

1. Need a root word? Ask. Propose one full English word. Refuse to start
   from `AI`, `MyApp`, or `*System`.
2. Need a second project? Default is no. `*.Tests` is the usual
   exception. Anything else is a user decision.
3. Need a folder? Only for a kind or feature the reader must see. Do not
   scaffold a clean-architecture tree into a three-file library.
4. Write the file: one type, name = type, namespace = path,
   file-scoped namespace, English identifiers.
5. Reviewing existing code: still report layout and naming defects even
   when the task is "just fix the logic". Do not deepen a wrong `src/`
   or `AI.*` tree with new files; flag it and ask to flatten / rename.

## 8. Quick check

1. Is the root one spoken English word, not a compound or acronym?
2. Are we in an app without fake `Company.Product.Layer` prefixes?
3. Is `.sln` next to project folders, with no `src/` or root `tests/`?
4. Is the test project `{Name}.Tests` sitting beside `{Name}`?
5. Does every `.cs` file contain one top-level type named like the file?
6. Does `namespace` equal the folder path?
7. Are contracts grouped instead of piled (`Interfaces/IExample.cs`)?
8. Did we ask instead of inventing a folder, a split, or a root word?
9. Did ASP.NET code avoid `.Api` and avoid a nested `Controllers` folder
   inside a `Controllers` project?
10. Would a stranger read Solution Explorer and know what the system is?

## Anti-patterns

```text
src/Intelligence/
tests/Intelligence.Tests/
AI/AI.Core/Services/Helpers/Utils.cs
ShopSystem.Api/Controllers/OrdersController.cs
IExample.cs + IExampleService.cs + enum Status  in one file
namespace Intelligence.DataAccess;
```
