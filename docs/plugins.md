# Plugins

Operators add in-process extensions as **plugins**. A plugin is one
directory, one `plugin.json`, and one assembly in its own load context.
It can contribute tools, a protocol client, approval classifiers, slash
commands, and hooks.

The host owns catalog registration, approval, workspace fencing, output
truncation, and timeouts. Plugin code does not bypass
`ToolInvocationPolicy`.

## What this is not

| Mechanism | Role |
| :--- | :--- |
| `PluginRegistry` | First-party in-process contributions (built-in tools and the six wire protocols). |
| External tool sets | Model-callable tools under `tools/`. They do not implement this contract and have no hooks. See [external-tools.md](external-tools.md). |
| Skills | Markdown instructions loaded through the `skill` tool. |
| MCP | Deferred. It is not a plugin runner. |

## Identity

The plugin has no JSON identity field. The plugin **is** its directory
under `plugins/`.

```text
~/.crystal/plugins/<directory>/plugin.json
<workspace>/.crystal/plugins/<directory>/plugin.json
```

Overlay key: the directory name. A project directory replaces the home
plugin of the same name as a whole. Directory names are 1–64 characters,
start with a letter, then `A–Z` `a–z` `0–9` `_` `.` `-`.

`IPlugin.Name` is the name shown by `/plugins`. It must be non-empty.
Two loaded plugins cannot share it.

## Discovery

`config.json` field `plugins` enables discovery (default `true`). When
`true`, the field is omitted from the written file. When `false`,
manifests are not scanned. `crystal run --plugins on|off` overrides that
switch for one process and is not written back.

`plugin.json` field `enabled` turns one plugin on or off (default
`true`). `"enabled": false` omits that plugin after overlay, with no
operator note. A missing, unreadable, or invalid manifest skips that
plugin and records an English note. The session still starts.

`/plugins` lists loaded plugins. `/plugins on`, `/plugins off`, and
`/plugins reload` persist the switch when it changes and reload catalogs.

## Manifest

`plugin.json` is JSON. Unknown fields are ignored.

```json
{
  "enabled": true,
  "assembly": "Acme.Plugin.dll",
  "type": "Acme.AcmePlugin"
}
```

`assembly` is a path inside the plugin directory. `type` is the public
entry type. It implements `CrystalCode.Plugins.IPlugin` and has a public
parameterless constructor.

## Contract

Reference `CrystalCode.Plugins`. Reference `CrystalCode.Tools` only when
a tool needs `ToolHostContext`. Do not reference `CrystalCode.Engine`,
`CrystalCode`, `CrystalCode.Display`, or `CrystalCode.Providers`.

`Contribute` returns tools, client factories, classifiers, commands, and
hooks. A null entry is omitted with a note. One bad plugin does not
stop the others.

### Tools

A tool implements `IPluginTool`: a model-facing `Name`, `Catalogs`,
an `ITool`, and an optional `IMultimodalTool` with the same name.
`Catalogs` is `PluginToolCatalogs.Plan`, `Work`, or `PlanAndWork`.
A tool must name at least one catalog. The same choice applies to its
multimodal tool.

Names match `^[A-Za-z][A-Za-z0-9_-]*$` (1–64 characters) and must not be
a built-in name. The definition name must match `Name`. Later duplicates
are omitted with a note.

Catalog order is built-in plugin tools, disk plugin tools, external
tools, then `skill` when enabled. A disk tool name is reserved against
external tools.

The plugin constructs the tool. Host facts arrive per call when the tool
implements `IHostTool` or `IHostMultimodalTool`. The tool does not
receive the question prompt.

### Protocol factories

`IPluginClientFactory.CanCreate` returns true only for protocols that
factory owns. A factory that returns true for `deepseek`, `openai`,
`responses`, `anthropic`, `gemini`, or `ollama` is omitted. Built-in
adapters stay first. A custom protocol is a token of 1–64 characters:
a letter, then letters, digits, `_`, or `-`. Put that token in
`providers.json` as `protocol`.

`PluginClientRequest` carries the endpoint, model sampling, and API key
for that call. Do not log the key. `CreateMultimodal` is used only when
the selected model accepts image input and no built-in factory owns the
protocol.

### Classifiers

`IPluginClassifier` runs only after the built-in switch misses. It
assigns risk and authority for an unknown tool. It does not reclassify
`read`, `bash`, or the other built-in names.

### Slash commands

`IPluginCommand` adds a `/name` verb. Built-in verbs and their aliases
win. Names use the same pattern as tool names. `IPluginOutput.Write`
and `Fail` become session notes.

## Hooks

Hooks run in plugin load order. The next hook sees the previous
replacement. The same pipeline wraps built-in tools, other plugins, and
external tools. A hook that throws is skipped with an English note.

| Hook | May | May not |
| :--- | :--- | :--- |
| `OnSessionStartedAsync` / `OnSessionEndedAsync` | Read workspace root, session id, and approval mode | Prompt the operator, or write `config.json` |
| `OnPrompt` | Append text to the instruction block | Replace Work, Plan, or Review |
| `OnUserMessageAsync` | Replace the user message before it is stored | Leave the stored message blank, or run for a side question |
| `OnTurnStartedAsync` / `OnTurnFinishedAsync` | Read the stored user text, mode, and, when the turn ends, the stop reason | Change the transcript |
| `RebuildModelAsync` | Drop or reorder items for one outbound call, including the text summarized during compaction | Change the live system prompt, add a tool call, split a call from its result, or write the archive |
| `TransformModelAsync` | Change user, assistant, or tool-result text for that same call, or drop image references already on an item | Reorder items, edit a system message, add an image, or write the archive |
| `OnToolCallAsync` | Replace the name or arguments | Change the call id, skip the call, or skip approval |
| `OnToolResultAsync` | Replace the text result and, on an image-capable turn, its images | Mark an approval as passed |
| `OnApproval` | Raise risk, or require another prompt | Lower the host risk, or auto-pass |
| `OnCompaction` | Append facts to the summary prompt or the summary body | Drop history, or replace the compactor |

A rewritten tool call is classified and approved again before it runs.
Text-only turns ignore images returned by `OnToolResultAsync`. Image
types are `image/png`, `image/jpeg`, `image/gif`, and `image/webp`.

`RebuildModelAsync` runs before `TransformModelAsync`. Both see the model
transcript for a work, plan, or side request, and the older turns about to
be summarized. They do not see the approval review. Returned image lists
may only name attachments already on that item. The stored transcript and
the archive stay as they were, except for text replaced by
`OnUserMessageAsync`.

Session start runs after the plugin load. Session end runs when the
session closes and before a `/cd` reload. `crystal run` closes the
session when the process finishes.

## Load context

Each plugin gets one non-collectible `AssemblyLoadContext`.

Shared assemblies come from the host: `Crystal`, `Crystal.Tools`,
`CrystalCode.Tools`, and `CrystalCode.Plugins`. Other dependencies must
resolve inside the plugin directory. The host does not probe the NuGet
cache or the workspace.

`CrystalCode`, `CrystalCode.Engine`, `CrystalCode.Display`, and
`CrystalCode.Providers` are refused, including as the entry assembly.
The context is not collectible. There is no hot reload, signing check,
or marketplace.

## Not included

- MCP.
- Collectible unload and hot reload.
- Replacing a built-in protocol, built-in tool, or built-in slash verb.
- Hooks on external tool sets.
