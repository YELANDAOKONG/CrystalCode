# External tools

Operators add model-callable tools as **tool sets**. A set is one
directory, one `tools.json`, and one runner (one executable prefix, or
one assembly and one load context). Each set contributes one or more
Crystal `ITool` values.

The host owns catalog registration, workspace fencing of model-supplied
paths, approval classification, output truncation, and timeouts.
Operator code never bypasses `ToolInvocationPolicy`.

Invocation channels:

- **exec + stdin JSON**: the fenced argument object for that call is
  written to the child stdin, then stdin is closed.
- **exec + argv**: listed scalar properties become extra process
  arguments. Operator-authored command arrays are never interpolated.
- **dotnet**: a class library whose public `Crystal.Tools.ITool` types
  are all loaded in one isolated `AssemblyLoadContext`.

Exec may use stdin, argv, or both. Dotnet does not use stdin or argv.

## What this is not

| Mechanism | Role |
| :--- | :--- |
| `IPlugin` / `PluginRegistry` | First-party in-process contributions (built-in tools, DeepSeek, OpenAI). Unchanged. |
| `~/.crystal/plugins/` | Reserved. External tools do not live there and do not implement `IPlugin`. |
| Skills | Markdown instructions loaded through the `skill` tool. They do not become extra `ITool` entries. |
| MCP | A product exclusion. |

First-party code contributions stay on `PluginRegistry`. Operator tools
are discovered as tool sets. Dotnet runners load **class libraries from
the set directory only**, with the dependency rules below. That is not
a general plugin loader.

## Identity

The tool set has no JSON identity field. There is no root `name` and no
`id`. The set **is** its directory under `tools/`.

Overlay key: the directory name. `<workspace>/.crystal/tools/Acme.Tools/`
replaces `~/.crystal/tools/Acme.Tools/` as a whole set. Lists are not
merged. Directory names may include uppercase. Pattern: 1–64 characters,
start with a letter, then `A–Z` `a–z` `0–9` `_` `.` `-`. No space, no
`/`. On Windows the filesystem is case-insensitive; two folders that
only differ by case are the same set.

Root `name` is not a field of this format. Like any other unrecognized
root key, it is ignored.

Each **tool** has a model-facing name: exec `tools[].name`, or dotnet
`ITool.Definition.Name`. That is what Crystal and the provider send. It
is not `<directory>/<tool>`. A slash is not a legal OpenAI-compatible
function name. The host does not compose a qualified id. Tool names
match `^[A-Za-z][A-Za-z0-9_-]*$` (1–64 characters) and must not be a
built-in name.

One-tool exec shorthand (no `tools` array): the tool name defaults to
the directory name. A different model-facing name requires a
one-element `tools` array. Shorthand therefore requires a directory
name that is also a legal tool name (no `.` in the directory).

Tool names must be unique in the process catalog. Built-ins (`read`,
`glob`, `grep`, `todowrite`, `question`, `edit`, `write`, `bash`,
`skill`) win. An exec set whose tool name is reserved is skipped. A
dotnet type whose `Definition.Name` is reserved is omitted. Two sets
that both contribute `deploy` collide the same way: the later name is
omitted with an English note. If authors want a prefix, they put it in
the tool name (`Acme_deploy`).

## Discovery

Crystal-owned paths only. OpenCode and Claude trees are not scanned for
tools.

```text
~/.crystal/tools/<directory>/tools.json
<workspace>/.crystal/tools/<directory>/tools.json
```

Every immediate subdirectory that contains `tools.json` is a candidate
set. The session enters the terminal frame first, shows `Loading Tools`
on the progress row, then scans and loads. `/cd` reloads the same way
from the new workspace.

`config.json` field `externalTools` enables the feature (default
`true`), matching `skills`. When `true`, the field is omitted from the
written file. When `false`, manifests are not scanned.

`tools.json` field `enabled` turns one set on or off (default `true`).
`"enabled": false` omits that set after overlay, with no operator note.
A project set can disable a home set of the same directory name this
way. The manifest is still parsed; an invalid disabled file is skipped
with a note.

A missing, unreadable, or invalid manifest skips that **set** and
records an English operator note. The session still starts. A set that
loads with several tools and fails one name omits only that name when
the failure is per-tool (reserved or duplicate name). Failures that are
per-set (missing DLL, invalid runner, duplicate names inside one exec
set) omit the whole set.

## Tool set vs tool

| | Tool set (directory + `tools.json` root) | Each contributed tool |
| :--- | :--- | :--- |
| Identity | Directory name | Model-facing `name` / `Definition.Name` |
| Enabled | `enabled` on the set (default `true`) | Same runner; no per-tool switch |
| Runner | One `exec` or `dotnet` | Same runner |
| Exec command | Shared argv prefix | Optional extra argv suffix (subcommand) |
| Dotnet | One assembly, one ALC | One public `ITool` type |
| `catalogs` | Default for every tool | Override |
| `pathArguments` / `argv` / schema | Defaults when there is a single exec tool | Required per exec tool when several exist |

## Catalogs (Plan / Work)

The product has two **tool catalogs**, Plan and Work. Approval modes
(Default, AutoEdit, Review, Full) are not catalogs. Do not put
`review`, `default`, or `full` in this field.

Field name is `catalogs`, not `modes`.

```json
"catalogs": ["plan", "work"]
```

The array is a set. `["plan", "work"]` and `["work", "plan"]` both mean
the tool is registered in Plan **and** in Work. `["plan"]` is Plan
only. `["work"]` is Work only.

Closed members: `plan`, `work`. Duplicates are ignored. Unknown values
refuse the set (or that tool override). An empty array refuses: a tool
that is in neither catalog must not be listed.

Default when `catalogs` is omitted: `["plan", "work"]`. Installing a
set opts it into the whole product unless the author narrows it.

A set-level `catalogs` is the default for every tool in the set. A
tool-level `catalogs` replaces that default; it does not merge.

Built-in Plan is still without `edit`, `write`, and `bash`. External
tools listed for Plan are extra entries. They keep the Write +
Workspace floor, so Plan is no longer built-in reads only once such a
tool is installed. Approval still runs. Plan + Full can auto-pass a
workspace-bounded write from an external tool. Authors who want
Work-only set `"catalogs": ["work"]`.

First-party `IncludeInPlan` on `IToolContribution` stays as it is.
External sets do not go through `PluginRegistry`.

## Manifest

`tools.json` is JSON. Unknown fields are ignored, including a root
`name`. Required at the root: `runner`.

Single exec tool (shorthand — one contributed tool, model-facing name
defaults to the directory name):

```json
{
  "runner": "exec",
  "description": "Ship the current workspace to a named environment.",
  "schema": {
    "type": "object",
    "properties": {
      "environment": { "type": "string" },
      "path": { "type": "string" }
    },
    "required": ["environment"]
  },
  "command": ["deploy", "release"],
  "stdin": true,
  "argv": {
    "environment": "--env",
    "path": "--path"
  },
  "pathArguments": ["path"],
  "timeoutSeconds": 120
}
```

Exec set with several tools (shared binary, subcommand dispatch):

```json
{
  "runner": "exec",
  "command": ["acme"],
  "stdin": true,
  "timeoutSeconds": 120,
  "tools": [
    {
      "name": "acme_deploy",
      "description": "Ship the current workspace to a named environment.",
      "schema": { "type": "object", "properties": { "environment": { "type": "string" } }, "required": ["environment"] },
      "command": ["deploy"],
      "argv": { "environment": "--env" }
    },
    {
      "name": "acme_inventory",
      "description": "List inventory for this workspace.",
      "schema": { "type": "object", "properties": {} },
      "command": ["inventory"]
    }
  ]
}
```

Dotnet set (all public `ITool` types; optional per-tool-name overlay).
Tool names come from `Definition.Name`:

```json
{
  "runner": "dotnet",
  "assembly": "Acme.Tools.dll",
  "tools": {
    "acme_inventory": {
      "catalogs": ["plan"],
      "pathArguments": ["path"]
    }
  }
}
```

| Field | Layer | Meaning |
| :--- | :--- | :--- |
| `name` | Tool only | Model-facing tool name. Exec `tools[]` required. Shorthand defaults to the directory name. Dotnet from `Definition.Name`. A root `name` is unrecognized and ignored. |
| `runner` | Set | `exec` or `dotnet`. |
| `enabled` | Set | Boolean. Default `true`. `false` omits the whole set after overlay. |
| `command` | Set and/or tool | Exec argv. Set prefix then tool suffix. No substitution. |
| `stdin` | Set | Exec only. Boolean, default `true`. Applies to every exec tool in the set. |
| `argv` | Tool (or shorthand root) | Map of schema property name to one flag string. |
| `pathArguments` | Tool (or shorthand root) | Property names treated as filesystem paths. |
| `timeoutSeconds` | Set | Default 120, same as bash. |
| `catalogs` | Set default, tool override | `plan` and/or `work`. Default `["plan", "work"]`. Both members means both catalogs. |
| `description` / `schema` | Each exec tool | Required for exec. Dotnet takes these from `ITool.Definition`. Overlay does not replace them. |
| `assembly` | Set | Dotnet only. File name relative to the set directory. |
| `types` | Set | Dotnet only. Optional allowlist of type names. Default: every public `ITool`. |
| `tools` | Set | Exec: array of tool objects. Dotnet: optional map keyed by `Definition.Name` for overlays (`catalogs`, `pathArguments`). |

Stdin is always the arguments object for the one call. There is no JSON
envelope (`{ "tool": "...", "arguments": { } }`). Which exec tool ran
is expressed by argv (shared prefix + per-tool suffix).

Shorthand vs `tools`: an exec set may use the shorthand root fields
**or** a `tools` array, not both. Mixing refuses the set. A `tools`
array with one element is valid and is not shorthand.

## Safety floors (all runners)

A process or loaded assembly is at least **Write + Workspace**. Path
properties in `pathArguments` are resolved with the same fence as
`read` / `write`. Credential paths become Forbidden. Paths outside the
workspace become OutsideWorkspace. The rewritten absolute path is what
the child or assembly sees.

Host classification looks up the external tool by its model-facing
name. It does not fall through to "Unknown tool" after a successful
load.

Full auto-passes workspace-bounded Write for any such tool, not only
built-in `write` / `edit`. AutoEdit still auto-passes only those
built-in names. Default and Review still treat external Write as a
side effect.

Secrets never appear in `tools.json`. The host does not inject
credentials into the child environment or into a loaded assembly.
Exec children inherit the host process environment. Dotnet assemblies
must not receive secrets via `Environment.SetEnvironmentVariable`
(process-wide). Do not log argument JSON that may contain secrets.
Stdout and stderr are concatenated and truncated the same way as bash
(`MaximumToolOutputCharacters`).

## Exec: stdin JSON and argv

The host starts the process with `ProcessStartInfo.ArgumentList` (no
`bash -lc`, no one-line command string). Working directory is the
workspace root.

A bare executable name is resolved inside the set directory when that
file exists; otherwise it may PATH-search. A relative path that
contains a directory separator must stay inside the set directory.
An absolute path is used as given.

Final argv is: set `command` + this tool's `command` + mapped flags
from this tool's `argv` map. For shorthand, there is no extra suffix.

**Stdin JSON** (`stdin: true`, default): write the fenced argument
object as UTF-8 JSON without a BOM, then close stdin. Same body for a
one-tool or many-tool set. The child already knows which subcommand it
is from argv.

**Argv** (`argv` map): for each listed property that is present, append
`flag`, then the scalar string form. Allowed JSON kinds: string,
number, boolean. A string array appends `flag` `value` for each
element. Objects, nulls, and mixed arrays refuse the call with a
model-visible English error. Property order follows the `argv` object
order in the file.

**Both**: stdin gets the full fenced object and argv gets prefix,
suffix, and mapped scalars.

**Stdin off**: stdin is still redirected and closed so the child does
not block.

Non-zero exit is `ToolResultStatus.Failure`. Timeout kills the process
tree when the OS allows it. stdout then stderr are concatenated,
truncated, and returned as `ToolOutput` text.

Several exec tools should live in one set when they share a binary.
That is the same unit as a multi-`ITool` assembly: one install, one
ALC or one executable, many catalog entries.

## Dotnet: every public `ITool`

The assembly is a **framework-dependent class library**, not a second
self-contained runtime and not `IPlugin`.

Compile against `Crystal.Tools` (and the `Crystal` reference it
requires). Implement `Crystal.Tools.ITool` with a public parameterless
constructor.

The host loads the assembly once, then **adds every matching type**:

- Public, non-abstract, non-generic class
- Implements `ITool` (host identity, see load context)
- Optional `types` allowlist: if present, only those type names; names
  in the list that are missing refuse the set
- Skip nested types unless they are public
- Zero matching types refuses the set
- Two types with the same `Definition.Name` refuse the set

Do not require each type to be listed in `tools.json`. The JSON overlay
map is optional. Keys in `tools` that do not match a loaded
`Definition.Name` refuse the set (typo). Types not mentioned still
register with set defaults. Overlay may set `catalogs` and
`pathArguments`; it does not replace the type's name, description, or
schema.

The host does not put operator `ITool` instances in the catalog raw.
Each one is wrapped:

1. Rewrite `pathArguments` on the `ToolCall` (overlay or empty).
2. Apply timeout via the cancellation token the session already
   threads.
3. Truncate `ToolOutput` text.
4. Run approval before `InvokeAsync`, same as every other tool.

The wrapper is the catalog entry. `AssemblyLoadContext` is not a
sandbox.

Operator types must not reference `CrystalCode`, `CrystalCode.Display`,
or `Spectre.Console`. No slash commands, client factories, or
classifiers from the assembly.

### Publish layout

Operators publish into the set directory:

```text
dotnet publish -c Release -o ~/.crystal/tools/<directory>
```

The project is `OutputType=Library`, `TargetFramework=net10.0` (or a
lower TFM the host can load). **Do not** publish `--self-contained` or
`--runtime` as a second RID-specific runtime. Native RID assets belong
under `runtimes/<rid>/native/` via a framework-dependent publish.

Expected files:

```text
<directory>/
  tools.json
  Acme.Tools.dll
  Acme.Tools.deps.json
  <copy-local managed dependencies>
  runtimes/            # optional native assets
```

`dotnet build` output is not enough: load requires `*.deps.json` and
the resolved dependency graph that `publish` writes.

The release host is **self-contained single-file**
(`PublishSingleFile=true`, not trimmed, not Native AOT). Shared
contract types (`Crystal`, `Crystal.Tools`) live in the host bundle.
They are not files next to `CrystalCode`. Authors compile against
`Crystal.Tools` from source or a future pack; at runtime those
identities must come from the **host load context** that already
loaded `Crystal.Tools` (`typeof(ITool).Assembly`), never from a DLL
copied into the tool folder. Do not call
`AssemblyLoadContext.Default.LoadFromAssemblyName` for those contracts:
in testhost isolation that can load a second copy and `is ITool` fails.

### Load context

One `AssemblyLoadContext` per **tool set** (directory), created at
session start, live for the process. All `ITool` types from that
assembly share it. Contexts are not collectible. Two sets never share
an ALC, so their private copies of Newtonsoft cannot collide.

Do not use `Assembly.LoadFrom` / `LoadFile` on the default context.

Algorithm for `Load(AssemblyName name)`:

1. If `name` is `Crystal` or `Crystal.Tools`, return the already-loaded
   host assembly (`typeof(ChatMessage).Assembly` / `typeof(ITool).Assembly`).
   Never load it from the set directory, even if `Crystal.Tools.dll` is
   sitting there.
2. If `name` is a **shared** framework assembly already loaded in the
   host context, return that instance.
3. Otherwise call `AssemblyDependencyResolver` constructed from the
   set's main DLL path (`*.deps.json` beside it). If it returns a path
   **inside the set directory**, `LoadFromAssemblyPath`.
4. Otherwise fail the **set** load with an English message naming the
   missing assembly. Do not probe the NuGet global cache, the GAC, or
   the workspace.

Unmanaged probe: `ResolvingUnmanagedDll` uses
`resolver.ResolveUnmanagedDllToPath`. Same directory fence.

**Shared** means: `Crystal`, `Crystal.Tools`, and every `System.*` /
`Microsoft.*` assembly already loaded in the host context that owns
`typeof(ITool).Assembly`, plus `netstandard` and `mscorlib`.

**Private** means everything else, including Newtonsoft.Json. The
`ITool` boundary is `ToolCall` / `ToolOutput` / `JsonElement`.

If the resolver points at `Crystal.dll` or `Crystal.Tools.dll` inside
the set folder, ignore that path and go to step 1.

Single-file implications:

- `typeof(ITool).Assembly` must be available after host startup. Shared
  contracts are taken from that host context, not probed from disk.
- Do not enable trimming or Native AOT on the host while this runner
  exists.
- The set ALC must not try to load `CrystalCode` from the single-file.

## Catalog composition

`WorkspaceCatalog` is: built-in plugin tools, then every contributed
external tool whose `catalogs` contains Plan or Work respectively, then
`skill` when enabled. External tools are not registered through
`PluginRegistry.Add`. The external classifier is appended to the
session's classifier list.

## Not included

- MCP, HTTP/OpenAPI runners, argv string templates, stdin dispatch
  envelopes.
- Qualified model-facing names (`Set/tool` or automatic `Set_tool`
  prefixes).
- Loading `IPlugin` from `~/.crystal/plugins/`.
- Collectible unload, hot reload, signing, marketplace.
- Host-injected secrets into child processes or in-process assemblies.
- A new contracts project. Dotnet tools compile against
  `Crystal.Tools`.
