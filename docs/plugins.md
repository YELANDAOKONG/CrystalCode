# Plugins

Operators add in-process extensions as **plugins**. A plugin is one
directory, one `plugin.json`, and one assembly in its own load context.
It can contribute tools, a protocol client, approval classifiers, slash
commands, hooks, raw hooks, and prompt placeholders. After the host has
loaded plugins, external tools, and skills, it calls `Attach` with a
read-only snapshot of those catalogs, then `AttachSession` with the live
session and review model. A plugin that implements `IPluginModelClient`
also receives clients for those models.

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

`/plugins` lists loaded plugins and any directory that is not loaded.
`/plugins on`, `/plugins off`, and `/plugins reload` persist the discovery
switch when it changes and reload catalogs. `/plugins enable <directory>`,
`/plugins disable <directory>`, and `/plugins show <directory>` change or
show one manifest. Prefix `home` or `project` to choose that tree. Without
a prefix, a project directory wins over Home. The command reloads catalogs
after a change. It does not create a directory. A broken manifest is left
unchanged.

`crystal plugins list`, `show`, `enable`, and `disable` do the same outside
a session. `--source home|project` chooses the tree. `--workspace` chooses
the project root. `--home` chooses the data directory. The default output
is a Spectre.Console table. `--format text` prints the same fields as
aligned plain text. These commands write `enabled` in `plugin.json` and
do not write `config.json`. Turning one plugin on does not turn discovery
on. `crystal run --plugins` remains a process-only override.

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

`Contribute` returns tools, client factories, classifiers, commands,
hooks, raw hooks, and prompt placeholders. A null entry is omitted with a note. One bad plugin does not
stop the others.

`Attach` runs after `Contribute`, once plugins, external tools, and skills
are loaded, and again when those catalogs reload. It runs before the
session-start hook. The default method does nothing. Reading the snapshot
inside `Contribute` is too early: the host has not finished the other
catalogs.

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

### Placeholders

`IPluginPlaceholder` adds a `{{name}}` token. The name is 1–64 characters:
a letter, then letters, digits, or `_`. Matching is case-insensitive. The
host rejects a name it already owns. Two plugins cannot share a name; the
first in load order wins and the later one is omitted with a note.

The host calls `Resolve` each time it binds a template, including
attachments, Review, and compaction. The context carries the mode,
workspace, session id, approval mode, provider, model, the current
catalog snapshot, and the live session and review model. Strings the host
does not have yet are empty. The
returned text is inserted as-is. Tokens inside that text are not expanded
again. A throw, or a missing return, leaves the original token and records
an English note.

Before the first `Attach`, the snapshot on that context is empty.

### Catalog snapshot

`IPluginEnvironment` is read-only. It does not enable, disable, invoke, or
reorder anything, and it does not declare a load dependency.

| List | Contains |
| :--- | :--- |
| `Plugins` | Every discovered plugin directory, including this plugin. Directory, source (`home` or `project`), display name, enabled, effective, loaded, and skip reason. No assembly path, type name, or manifest path. |
| `ToolSets` | Every discovered external tool set, with the same directory fields. |
| `ExternalTools` | Tools that loaded: name, set, source, and whether the tool is in Plan, Work, or both. |
| `Skills` | Skills the `skill` tool can load right now: name and description. One entry per name. Empty when Skills is off. No file body and no path. |

`enabled` is null when the manifest could not say. Display name is empty
until that plugin loads. Built-in tools are not listed.

### Model facts

`AttachSession` runs with `Attach`, after `Contribute`, and again when
that plugin instance is loaded again. The object stays live. `/model`,
`/approval model`, and `/thinking` show up on the next read. The host does
not call `AttachSession` again for those changes.

`Session` carries the provider, protocol, model name, context window,
maximum tokens, temperature, top-p, image input, and the current thinking
gear. `default` and `off` are host sentinels. Other values are effort
names such as `low` or `high`.

`Review.Independent` is true only when review uses its own model. That
model's thinking gear is `default`. While the switch is off, review uses
the session model, `Model` is null, and a saved provider or model name is
omitted. The facts include no API key, endpoint, organization, or project.

`PluginPlaceholderContext.Models` is the same live view. It is empty until
the session publishes it.

### Model clients

A model call spends the operator's credentials. It does not enter the
transcript, the approval path, or the turn budget. The entry type receives
clients only when it implements `IPluginModelClient`. The host names that
plugin when it loads, for example `Plugin 'Acme' can call the session and
review models.` Built-in plugins do not receive these clients.

`AttachClients` runs after `AttachSession`. The clients are new instances
on the same provider and model. They are kept apart from the instances
inside a turn or a review.

| Member | Meaning |
| :--- | :--- |
| `Session` | Text client for the session model. |
| `Images` | Image client for the session model, or null when that model does not accept images. |
| `IndependentReview` | True only when review uses its own model. Reading it does not create a client. |
| `Review` | Text client for the review model while `IndependentReview` is true. Null while review uses the session model. |

When `IndependentReview` is true and the review client cannot be created,
reading `Review` fails. The host leaves the session client in place.

The first read creates a client. Later reads return that instance until
the host drops it. The host drops the session and image clients when it
rebuilds the session client, and drops the review client when it rebuilds
the review client or the switch turns off. Read the property again after
that.

`IPluginClientFactory` remains the other direction. It builds a client for
a protocol the built-in adapters do not own.

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
| `TransformModelAsync` | Change user, assistant, or tool-result text for one outbound call, or drop image references already on an item | Reorder, add, or drop items, edit a system message, add an image, or write the archive |
| `OnModelResponseAsync` | Read the purpose, finish reason, returned items, and usage of one completed work, plan, side, or compaction call | Change the transcript, the archive, or the response the host uses |
| `OnToolCallAsync` | Replace the name or arguments | Change the call id, skip the call, or skip approval |
| `OnToolResultAsync` | Replace the text result and, on an image-capable turn, its images | Mark an approval as passed |
| `OnApproval` | Raise risk, or require another prompt | Lower the host risk, or auto-pass |
| `OnCompaction` | Append facts to the summary prompt or the summary body | Drop history, or replace the compactor |

A rewritten tool call is classified and approved again before it runs.
Text-only turns ignore images returned by `OnToolResultAsync`. Image
types are `image/png`, `image/jpeg`, `image/gif`, and `image/webp`.

`TransformModelAsync` sees the model transcript for a work, plan, or side
request, and the older turns about to be summarized, after every raw hook
has run. It does not see the approval review. Returned image lists may only
name attachments already on that item. The stored transcript and the
archive stay as they were, except for text replaced by
`OnUserMessageAsync`.

`OnModelResponseAsync` runs after one of those calls returns and before the
host commits that candidate or runs its tools. Approval review does not
call it. The host keeps the candidate it received.

`TransformModelAsync` changes only the items sent on that one call. The
stored transcript and the archive stay as they were. Rewriting user,
assistant, or tool-result text can make the model answer from a
conversation the operator did not store. A replacement that reorders,
adds, or drops items, changes a role or a tool call, or edits a system
message is skipped with an English note. Structural changes belong to raw
hooks.

Session start runs after the plugin load, and again when another coding
session becomes active. Session end runs when the session closes, when
`/clear`, `/resume`, or `/fork` replaces it, and before a `/cd` or
`/space` reload. The end callback still carries the workspace and session
being left. A resume that also changes workspace is one end and one start.
`crystal run` closes the session when the process finishes.

## Raw hooks

A raw hook is a privileged, low-level extension point. `IPluginRawHook`
is a separate interface. A plugin registers raw hooks on
`PluginContribution.RawHooks`, apart from `Hooks`. The operator does not
approve them. The host writes one note for each plugin that registers
one, for example `Plugin 'Acme' registered a raw hook.`

Raw hooks run in plugin load order. The next raw hook sees the previous
replacement. A raw hook that throws is skipped with an English note. The
host does not hold a raw hook to the rules that bind ordinary hooks. It
refuses only what it cannot represent.

| Raw hook | May | May not |
| :--- | :--- | :--- |
| `RebuildModelAsync` | For one outbound call, drop, reorder, add, or rewrite items of any kind. That includes the live system prompt, roles, tool calls, tool results, and reasoning, and it may split a call from its result | Repeat an item id, return empty reasoning text, name an image the session does not hold, add an image to a request that cannot carry images, or write the archive |
| `RewritePrompt` | Replace the composed system text for `work`, `plan`, `review`, or `compaction` with any string, including an empty one. Work and plan text becomes the live system message and is archived | Write `~/.crystal/prompts` |
| `RewriteCompaction` | Replace the compaction user prompt, or the summary body that is stored, with any string, including an empty one | Choose which history is folded, or replace the compactor |
| `RewriteApproval` | After ordinary approval hooks, set any risk, any authority, any summary, and either prompt requirement. The approval policy then uses that classification | Skip that policy |

`RebuildModelAsync` sees the same requests as `TransformModelAsync` and
runs first. The stored transcript and the archive stay as they were.

- **Ids.** An item keeps the id the host gave it. A new item needs an id
  the request does not use. An id that returns with a different kind of
  item is a new item.
- **New items.** A new tool result ignores its `Name`, which the host
  derives from the call. New reasoning carries text only, with no
  provider state. Rewriting the text of existing reasoning drops that
  state too.
- **Images.** The host stores image bytes in the session and references
  them by marker. A raw hook can keep, drop, or move an attachment that is
  already in the request, with the same media type. It cannot create an
  image. `PluginModelImage` carries no bytes. Support for new images is
  planned. Only user messages and tool results carry images to the model.
  Images on assistant and system messages become plain text.
  `PluginModelRequest.AcceptsImages` is true only for a work or plan call
  on an image-capable model. Text-only turns, side questions, and
  compaction never send images, so the host skips a replacement that adds
  one there.
- **Provider rules.** The host does not check what a provider accepts. A
  request that splits a tool call from its result, or moves the system
  prompt, can be rejected by the provider. When a work or plan call fails
  after a raw hook changed its request, the host writes a note such as
  `Raw hook 'Acme' changed this model request. The failure may be related.`
  The note is a hint, not a diagnosis.
- **Prompt text.** `OnPrompt` still only appends. `RewritePrompt` then
  replaces the composed system text for that mode. Any returned string is
  used, including an empty one. Work and plan text is what the session
  stores. The files under `~/.crystal/prompts` stay as the operator saved
  them. `RebuildModelAsync` can still change the system item of one outbound
  call after that, without writing the archive.
- **Compaction text.** `OnCompaction` still only appends. `RewriteCompaction`
  then replaces the full compaction user prompt or the stored summary body.
  An empty replacement is kept.
- **Approval.** `OnApproval` can still only raise risk or require another
  prompt. `RewriteApproval` then replaces the classification and may lower
  risk, change authority, change the summary, or clear the prompt
  requirement. The approval policy uses the result.

More hook methods will be added later. A new method joins `IPluginHook` or
`IPluginRawHook` when its behavior is defined. This build does not reserve
empty methods for them. Whether plugins declare dependencies on one another,
and related loading questions, are under consideration and are not decided.

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
- A module system below plugins and raw hooks. A later one may reach the
  host through reflection or another mechanism. Its behavior and ownership
  are not defined, and this build does not reserve types for it.
