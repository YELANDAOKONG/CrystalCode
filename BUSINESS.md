# Product Definition

## Mission

CrystalCode is a local coding agent for real repositories. A developer runs
it in a workspace, talks to a model, and lets the agent inspect and change
code under explicit modes (Plan, Work, and operator-defined custom modes) and
a risk-aware approval policy.

It is a product, not a library demo. Crystal supplies protocol types and tool
dispatch. This repository supplies the coding experience: prompts, tools,
approval, compaction, persistence, providers, and the terminal UI.

## Intended users

Developers who want a Claude Code / Codex-class coding loop on .NET, backed by
the Crystal library they already own.

## Current product

The current product is a terminal application with an HTTP server for other
front ends. `crystal` opens the terminal. `crystal server` serves the same
session capabilities over HTTP (see below). `crystal bridge` serves one
session to an Agent Client Protocol (ACP) editor over stdio. Those three are
the operator surfaces. `crystal space` opens that
terminal in the operator space at `~/.crystal/space`. `crystal` and
`crystal space` accept the same session overrides as `crystal run`, except
the task text, `--format`, `--show-thinking`, `--space`, and
`--workspace-trust`. Those overrides shape that process only and are not
written to `config.json` or `prompt.json`. A later preference command still
writes `--provider` and `--model` when they were passed. `crystal run` is a
headless entry for one task and has no operator. `crystal version` prints
the build identity in two plain-text sections and exits. The engine behind the terminal and `crystal run` is a
separate library with no terminal code. The product can:

- stream a model turn with tool calls, and queue follow-ups while it runs;
- configure per-turn model-call, tool-call, and wall-clock budgets, including
  unlimited budgets;
- configure the built-in bash per-command timeout, including unlimited;
- start session-local background shell tasks in Work, inspect their status and
  bounded logs, and stop them with child-process cleanup;
- retry a failed model round on rate limits, server errors, timeouts,
  network faults, and incomplete streams, waiting with backoff, a
  countdown on the progress row, and a transcript note;
- switch the configured provider and model from `/model` without restarting;
- declare per-model context budgets in `providers.json` with `contextWindows`,
  switch them through `/context`, remember each model's choice, and show the
  selected budget in the default or custom status bar. The selected budget
  drives local compaction and context accounting; it does not change provider
  pricing or enable provider-specific long-context features;
- declare per-model service tiers and speeds in `providers.json`, switch them
  through `/tier` and `/speed`, remember each model's choice, and show the
  selected values in the default or custom status bar;
- switch Plan (built-in reads; no edit, write, apply_patch, or bash) and Work
  (edit, write, multi-file patches, shell, and session-local background shell
  tasks); operator tool sets
  may add extra catalog entries to either;
- define custom modes as directories `modes/<name>/` under `~/.crystal` and
  `<workspace>/.crystal`, each with `mode.json` and `prompt.md`. A custom mode
  names the built-in tools it exposes, carries its own system prompt, and is
  listed after Plan and Work. `enabled` defaults to false; a workspace entry
  replaces the entire Home entry of the same name, including when it is
  disabled or invalid. Plan, Work, and `default` are reserved names. A mode
  does not change approval: every call still goes through the selected
  approval mode and the host classification floor, so listing `bash` in a
  mode does not make shell calls pass. Tab and Shift+Tab cycle Plan, Work, and
  each enabled custom mode in that order. `/mode` lists modes, `/mode <name>`
  switches, and `--mode <name>` on `crystal`, `crystal space`, and `crystal run`
  starts in one for that process only. The current mode is saved with the
  session. A resumed or forked session whose custom mode is missing or
  disabled falls back to Work with a note. A custom mode is not a child Agent,
  prompt set, or prompt attachment, and it is not an approval mode;
- persistently disable named built-in tools independently for Plan and Work
  through `config.json` `disabledBuiltInTools` and `/tools builtin`. The legacy
  array form applies to both modes. `--disable-tool` remains a process-only
  overlay on `crystal`, `crystal space`, and `crystal run`; an unqualified name
  applies to both modes, while `plan:<name>` and `work:<name>` target one mode.
  `/tools builtin session` disables names for the current session only: they
  are saved in that session file, restored by resume, and not copied by fork
  or kept by `/clear`. Unknown names refuse to start or change. The active
  mode exposes its merged disabled list (Home, process, and session) through
  `{{disabled_tools}}`, and the name stays reserved;
- approve side effects manually, by a reviewing model, or by Full pass-through.
  The reviewing model can have its own provider, model, thinking gear,
  service tier, and speed. In Review and Audit, an optional extended approval
  model gives a call the primary reviewer allowed a final safety check. It
  defaults to allow and blocks or escalates only for a concrete problem. It
  runs on every review allow by default, or only when the primary risk level
  reaches a configured `medium` or `high`. It may see the primary verdict or
  judge on its own, and it can allow, deny, or send the call to the operator.
  Extended approval is not an approval mode and is not the product review in
  `TODO.md`.
  Full auto-passes all tool calls, including workspace-external paths, session
  read-only directories, elevated or forbidden classifications, and forced
  prompts. Credential paths remain prohibited. Tool catalogs, argument
  validation, OS permissions, cancellation, and configured budgets still apply;
- ask one or more operator questions in one request, with described choices,
  single or multiple selection, optional custom answers, and confirmation;
- select a color theme for the terminal. `config.json` `theme` is a built-in
  (`default`, which equals `dark`, `dark`, or `light`) or the name of a file in `~/.crystal/themes/<name>.json`, which maps color names
  to Spectre styles and overrides the built-in palette. `/theme` lists and
  selects it and writes `config.json`; `--theme <name>` on `crystal`,
  `crystal space`, and `crystal run` affects that process only. Themes are
  Home-only so a repository cannot change the interface. `crystal run`, plain
  text output, and other front ends do not use it;
- run an operator-authored command when the session waits for the operator,
  such as a desktop notification or a webhook. `operatorNotify` in
  `config.json` holds the command, an `enabled` switch, the events
  (`approval`, `question`, and `turn`), a delay, and a timeout. There is no
  built-in command and it is off until a command is set. An approval notifies
  only when the operator is asked, after any review; auto-pass and grants do
  not. `turn` notifies when a turn ends without a queued follow-up, and not
  after an interrupt. The command never blocks the prompt. `/notify` shows
  the setting, `/notify on|off` writes `config.json`, and `/notify test`
  runs the command once. `crystal run` never runs it;
- compact the context sent to the model when usage approaches the model
  window, or when the operator runs `/compact`. Manual compaction summarizes
  earlier turns without waiting for that threshold, prints the summary unless
  `showCompactionSummary` is off, and keeps the full conversation for resume,
  fork, and export. Automatic compaction prints `Compacted context` and, when
  `showAutomaticCompactionSummary` is on, the same summary. That switch
  defaults off and is omitted from `config.json` until enabled. `/compact summary`
  and `/compact auto` (`automatic` is the same) toggle those two switches or
  set `on` / `off`, and write `config.json`. They do not summarize.
  Optional microcompaction, off unless `microCompaction` is set, clears old
  successful tool results before each model call. `/compact micro` toggles it
  or sets `on` / `off` and writes `config.json`. It does not summarize.
  Resource trimming, on by default, bounds the resources a model request
  carries (images today) so a session that holds many tool-returned images
  does not exceed the provider's request size. A provider definition may set
  its own `resourceTrim` limits, because request size limits differ by
  endpoint. It only changes what is sent: the transcript, the
  saved session, resume, fork, and export keep every image, and the model
  sees an omitted image as its `[Image #N]` marker text. `resourceTrim` in
  `config.json` sets the limits, and `/trim` leaves older resources out on
  demand. Images the operator attached are kept unless `includeUserResources`
  is set. Audio and video are not covered because they are not supported yet;
- ask before the first interactive session in a directory, when workspace
  trust is on (the default). Trust is the git root when the workspace is
  inside a repository, and the workspace itself otherwise. A yes is
  remembered under `~/.crystal/trusted.json`. A no exits without recording
  a denial. `/cd` into an untrusted root asks again and stays put on no.
  `/trust` shows the switch, turns it on or off, or forgets the current
  root. Turning the switch off skips later prompts and keeps the ledger.
  The operator space at `~/.crystal/space` is trusted without a prompt and
  without a ledger entry. Trust stops at that directory and does not climb
  to a parent git repository. `/trust forget` leaves it trusted. A
  subdirectory follows the ordinary rule unless `space` itself is the git
  root. `crystal space` creates the directory when it is missing.
  `/space` switches the open session there and creates it the same way.
  `/cd` with a path and `/space` are refused while a turn is running.
  `/cd` with no path still prints the current workspace.
  `crystal run --space` runs one task there and creates it the same way.
  That flag cannot be combined with `--workspace`;
- serve one session over HTTP with `crystal server`, so that separate front
  ends, such as a web page or a desktop program, can drive it. An operator or
  a wrapper program starts it in a workspace with the same session launch
  options as `crystal`, plus `--workspace`, `--space`, and `--resume`. The
  front ends are not part of this repository. Commands are REST-style
  requests and events arrive as server-sent events. One process hosts one
  active session; `/clear`, `/resume`, `/fork`, `/cd`, and `/space` behave as
  in the terminal. Every engine capability is reachable: every slash command,
  Plan, Work, and custom modes, quick prompts, side questions, recall,
  interrupts, image upload and download, child inspection, export, and every
  event, including approval and extended approval. What a slash command
  lists as text (saved sessions, models, options, Agents, skills, MCP
  servers, plugins, and background tasks) is also readable as structured
  data, with no secret in it. Mode, approval, thinking, tier, speed,
  context, and model chosen during a turn apply on the next turn. Approval, the question
  tool, trust, and session and child choices reach clients as interactions;
  approval, trust, the workspace fence, and `ToolInvocationPolicy` are
  unchanged. Clients refresh from a snapshot and reconnect by event sequence
  number. A bearer token is required by default on every listener and never
  appears on the command line; `--auth none` turns it off with a warning.
  Tokens carry `observe`, `operate`, or `admin` scopes. A LAN listener without
  TLS is allowed with a warning. Host and Origin checks always apply. The
  protocol carries no version identifier, and its flags are not written to
  `config.json`. See [docs/server.md](docs/server.md);
- drive one session from an ACP editor, such as Zed, with `crystal bridge`.
  The editor starts it as a subprocess and speaks ACP v1 on stdin and stdout;
  stdout carries protocol lines only. It accepts the same session launch
  options as `crystal`, plus `--workspace` and `--space` as the fallback when
  a session request has no `cwd`. The editor can start, load (with the
  conversation replayed), resume, list, and close saved sessions, and
  `/clear` keeps the session id it knows. Prompts accept text, images,
  embedded resources, and resource links, including local image files; a
  block the session cannot use becomes a one-line mention instead of an
  error. Replies and reasoning stream as chunks, tool calls arrive with
  titles, kinds, locations, and diffs, permission requests go to the editor,
  todos become the plan, and usage, slash commands, and `/help`, `/status`,
  `/stats`, `/tools`, and `/todos` reports reach it. Mode, model, thinking,
  context, tier, speed, approval, the approval models, prompt set, Agents,
  microcompaction, and estimated tokens are editor config options that run
  the same slash commands as the terminal. The editor's MCP servers join the
  session and are never saved, its additional directories become the
  session's outside write roots, and the `question` tool, MCP elicitation,
  workspace trust, and `/resume` and `/child` choices are editor forms when
  it supports them. Approval, trust, the workspace fence, and
  `ToolInvocationPolicy` are unchanged; without forms an untrusted directory
  is refused with instructions, not trusted silently. Bridge never calls the
  editor's file system or terminal. Child Agents appear as progress on the
  parent `task` call, or as subagent sessions for an editor that opts in;
  child approvals reach the editor under the parent session unless it does.
  Compaction appears as ACP compaction updates for an editor that supports
  them. A saved session can be forked into a new one without changing the
  source. Every other session capability (snapshot, recall, the queue, modes,
  verbose output, images, quick prompts, side questions, children, export,
  compaction, and structured reports) is a `_crystal/*` request, and live
  state that ACP has no update for is a `_crystal/*` notification sent only
  to an editor that opts in. See [docs/bridge.md](docs/bridge.md);
- run one task without a terminal through `crystal run`, then exit.
  Flags on that command override provider, model, workspace, home,
  approval, the approval model and its thinking gear, service tier, and speed,
  extended approval and its model, gear, tier, speed, trigger, and
  primary-review switch, Plan, Work, or a custom
  mode, thinking, the selected model's context budget, service tier, speed,
  prompt set, prompt attachments,
  named prompt attachments, skills, external tools, rule files, and turn quotas for that process only.
  They are not written to `config.json` or `prompt.json`. Stdout is a readable plain-text trace, or one JSON object
  per line when `--format json` is set. Review and Audit still use the
  reviewing model. That model can be a separate provider and model, and
  the choice can be turned off.
  Anything that would ask the operator is denied. Directory trust for that
  command is its own switch, `--workspace-trust on|off`. Omitting it checks
  trust even when the interactive switch is off. `off` skips the check for
  that process and does not record the directory;
- manage multiple accessible directories per session through `/access` and
  repeatable `--allow-read` / `--allow-write` launch options. `read` permits
  built-in file reads and searches; `write` includes reads and lets file changes
  follow the selected approval mode. Outside Full mode, explicit read-only writes are rejected
  before grants or review. Rules use canonical absolute paths, with the most
  specific directory winning over parents and the implicit writable workspace.
  They are session metadata, not reusable approval grants. Resume restores the
  saved rules and does not reapply defaults. A new session, `/clear`, `/fork`,
  and a successful workspace change start from that workspace's defaults in
  `~/.crystal/access.json`, keyed by the canonical workspace root, unless this
  process passed `--allow-read` or `--allow-write`. Those flags are the complete
  seed for that process, replace the defaults, and are not written back. A
  refused or no-op workspace change keeps the current rules. The file lives
  under Home so a project cannot grant itself extra directories. It is not
  `config.json` and not `<workspace>/.crystal`.
  `/access` lists the current session rules. `/access add <path> read|write`
  adds or updates a session rule; `/access remove <path>` removes one using its
  recorded entry path even after a symlink is deleted or retargeted.
  `/access default` lists this workspace's defaults. `/access default add` and
  `/access default remove` change that file and the current session only.
  Invalid paths report path errors. A missing or credential default is skipped
  at seed time with a visible error and left in the file. A corrupt file or
  conflicting modes seeds nothing. Invalid saved access blocks resume with a
  visible error; in-session resume keeps the current session intact. Rule
  changes are refused during turns or compaction. Shell and extension processes
  are not sandboxed
  by these rules, and no additional catalogs or instructions are discovered.
  The same list is the host placeholder `{{directories}}`. It is empty when
  the session has no added rules. Built-in prompts do not include that slot
  and still append the list to workspace instructions when rules exist;
- persist configuration, permissions, and sessions under `~/.crystal`;
- recall submitted prompts from the composer with Up/Down and retain up to 200
  recent text-only entries across runs for the same workspace;
- return a just-submitted message to an empty composer with Ctrl+C while that
  turn has not shown model text, reasoning, a tool call, or a retry, and the
  follow-up queue is empty. A note, an error, or a slash command that runs
  during the turn, such as `/status` or `/help`, also ends that window. The
  message leaves the transcript and the turn stops as interrupted. Text typed
  before the turn finishes is kept after the returned message. The next Ctrl+C
  still clears the composer, and the press after that on an empty composer
  exits. While a follow-up is queued and the composer is empty, Ctrl+C first
  takes the newest queued message back into the composer and does not cancel;
  once the queue is empty it cancels as above. After the window ends, Ctrl+C
  only cancels and the message stays. A message whose trusted image markers are no
  longer held is not returned. `crystal run` does not return a message this way;
- edit a long prompt in a full-screen editor with F8 or `/edit` in the
  terminal. The edit returns to the composer and is never sent automatically;
- open quick prompts with F6 or `/quick` (alias `/shortcuts`) from user-level `~/.crystal/shortcuts/` (or the
  selected Home). Each open reloads direct UTF-8 `.md` files; the filename is
  the name and the full body is the prompt. Search matches names and bodies,
  arrows select, PgUp/PgDn or wheel scroll the preview, Tab switches Fill/Send,
  Enter applies, and Esc cancels. Every open defaults to Fill. Fill inserts at
  the cursor without replacing the draft; Send sends only the template and
  preserves the draft and its images. Busy sends wait in the follow-up queue
  without interrupting the current turn. Modal prompts do not open the picker.
  Only Send enters prompt history. Templates are ordinary user text: no shell,
  variable expansion, skill loading, or session-setting changes. Empty files,
  invalid UTF-8, slash-command bodies, unsupported controls, symbolic links,
  unreadable files, and files larger than 64 KiB are disabled with reasons.
  Up to 256 files are shown. A missing directory shows its path and creates
  no examples. There is no project override, in-TUI file editor, or run flag;
- select a reusable Home prompt set without changing the higher-priority direct
  prompt overrides in Home or the workspace; a set may replace Work, Plan,
  Review, the Review user turn, extended approval system and user text, topic
  naming, compaction, and image description;
- enable an ordered list of prompt attachments that append to the resolved
  Work, Plan, and Review text; directories are discovered in Home and in the
  current workspace, and the workspace copy wins when both exist. A repeated
  `--prompt-attachment <name>` flag appends those names for the process after
  the enabled set, even when a manifest says `enabled` false, and does not
  write `prompt.json`;
- list saved sessions for the current workspace or every workspace; resume the
  current workspace, a named directory, or every workspace, entering that
  directory when the choice names one; and fork into a new independent session;
- discover OpenCode-compatible agent skills and load them through the
  `skill` tool when Skills is enabled;
- discover operator tool sets under `~/.crystal/tools` and
  `<workspace>/.crystal/tools` and register them as extra catalog tools
  when External Tools is enabled;
- connect to MCP servers defined in `~/.crystal/mcp.json`, single-server
  definitions under `~/.crystal/mcp/<name>/server.json`, and their workspace
  equivalents when MCP is enabled. A Project server replaces a Home server of
  the same name as a whole. A server may be switched off with `enabled` and
  stays listed. Server tools are registered with `mcp__<server>__<tool>`
  names and need approval on every call; a server's own read-only claim lowers
  a call only where the operator chose `author` for that source. Resources use
  two host tools: `list_mcp_resources` and `read_mcp_resource`. Prompts become
  slash commands `/mcp__<server>__<prompt>` that submit the prompt text as a
  user message. Server elicitation requests go through the existing operator
  question panel and are cancelled in `crystal run`. The client declares the
  roots capability and answers with the current workspace root. Roots are
  deprecated in the newest protocol revision, so this is for servers that
  still ask. A changed tool list is applied after the turn, never during one.
  Sampling and OAuth are deferred. Session-level servers supplied by a front
  end are deferred until that front end exists.
  Connections start during session startup and the session waits for them:
  a 30-second limit applies to the connection and to listing tools, tool
  calls default to 120 seconds, and a server definition may set
  `"unlimited"`. A hung server therefore delays startup by at most that
  limit. Failed connections or tools write notes without ending the session.
  Servers reconnect on `/cd`, `/space`, and `/mcp reload`, stay connected
  through `/clear`, and stop when the session closes. `/mcp` and
  `crystal mcp` list, show, enable, and disable servers, and `/mcp` also sets
  the MCP switch and the per-source approval choice. They never print a
  server's command, arguments, environment, headers, or URL;
- load operator tool sets and plugins without freezing the operator surface.
  Loading runs off the front end's thread and the engine names each tool
  set, plugin, and MCP server as it starts loading. In the terminal, session
  start shows a separate startup page before the session frame: it lists
  what is loading and accepts only Ctrl+C, which cancels startup and exits.
  Keys typed there are discarded. In a live session (`/tools`, `/plugins`,
  `/cd`, `/space`, resume into another workspace) the frame keeps drawing,
  the progress row names the item, and the composer stays editable, but
  Enter, F6, and F7 do nothing until loading ends, so nothing typed
  becomes a queued turn or an error and the draft stays. Ctrl+C cancels a
  plain `/tools reload` or `/plugins reload` between items and keeps the
  previous catalog; other reloads run to the end because their settings
  or workspace have already changed;
- load operator plugins from `~/.crystal/plugins` and
  `<workspace>/.crystal/plugins` when Plugins is enabled, including
  tools, protocol clients, classifiers, slash commands, hooks, raw
  hooks, prompt placeholders, and process commands (admitted by the engine; no entry point runs them yet). After catalogs load, a plugin can read
  the discovered plugins, external tool sets, loaded external tools, and
  the skills currently available to the skill tool. A loaded plugin may
  also register custom status-line fields. They stay off the adaptive
  bar until the operator names them in `statusLine` or `/statusline`.
  On the custom status line the operator may mark a field `keep` (never
  hidden) or `hideBelow` a terminal width, and may list fields in
  `statusLineDrop` to hide first, all in `config.json`.
  A plugin can read the
  live session model and whether review is using its own model. A plugin
  that asks for model clients can call the session model and, while that
  switch is on, the review model, on clients kept apart from the turn.
  Hooks may revise a user message before it is stored and may
  revise the text of the outbound model request without changing the saved
  archive. A raw hook is a privileged extension point. It may rebuild one
  outbound request, replace composed prompt and compaction text with any
  string, and replace an approval classification with any risk, authority,
  summary, and prompt requirement. The session names each plugin that
  registers one. Ordinary hooks stay append-only for those prompts and
  cannot lower approval risk. One plugin or one external tool set can be
  enabled or disabled by directory name from the session or from
  `crystal plugins` and `crystal tools`. Those commands write that
  manifest's `enabled` field and do not write `config.json`. Their default
  output is a Spectre.Console table; `--format text` prints aligned plain
  text;
- honor an author-declared `approval: always` in a tool set for ordinary
  workspace-bounded calls, with the operator choosing per source whether
  declarations take effect through `externalToolApproval` in
  `config.json` (Home and Project each `author` or `host`; defaults are
  Home `author`, Project `host`); external dotnet tools may provide a
  side-effect-free pre-classifier when their write paths are embedded in
  structured arguments, and that classifier can only tighten classification,
  never replace the floor or the source policy. Classification is cancellable
  and has a separate two-second budget. Reusable grants cover ordinary
  workspace Read/Write only; a raised classification is reviewed again.
  A forced prompt always requires the operator, even after a review allow;
- use DeepSeek and OpenAI-compatible Chat Completions, OpenAI Responses,
  Anthropic Messages, native Gemini GenerateContent, and native Ollama Chat
  adapters, including user-added gateways;
- group one gateway's protocol endpoints under a single provider name and route
  each uniquely named model through its configured protocol;
- attach PNG, JPEG, GIF, or WebP images from the workspace, paste clipboard
  images on supported terminals, copy submitted images into Home as validated
  binary files, persist references with sessions, export available images inline
  in JSON, and
  carry generic plugin-returned images into the next model round when a model
  explicitly declares image-input support;
- record `audioInput` and `videoInput` on a model entry. Those flags are
  declarations only: the host stores them and publishes them to plugins, and
  it does not attach, persist, or send audio or video;
- configure an audio and a video description model (`audioModel`,
  `videoModel`) alongside the image model. That configuration is reserved
  for the deferred audio and video path and changes no behavior today;
- color pasted image markers and edit them as single composer units while
  identically spelled user text remains ordinary text; discard unsent images
  whose attached markers were deleted;
- register built-in tools and providers through an in-process plugin table.
  In-process plugin contributions may explicitly replace a reserved built-in
  tool for Plan, Work, or both while keeping the model-facing name. The host
  still owns approval, execution, cancellation, and budget boundaries.
  Approval classification is selected by that model-facing name, so a
  replacement is trusted not to perform effects beyond the replaced built-in's
  classification. External tool sets stay append-only: they cannot replace a
  built-in tool or adjust its model-facing description or input schema, and no
  such capability is planned. A disk plugin is executable code the operator
  installed, so it may also replace a built-in tool, take over a built-in
  slash verb, and take over a built-in protocol. Each takeover is noted in
  the session. Approval, trust, the workspace fence, and
  `ToolInvocationPolicy` still apply to its tool calls;

## Child Agents

Child Agents are delegated conversations inside a parent session. They are
available in Plan and Work when `agents.enabled` is true; the default is false.
The model uses one `task` tool with an Agent name, short label, task prompt,
and optional child session id. A call without an id creates a new child even
when its Agent name matches another child. A call with an id continues only
that parent's child, with the same Agent. It cannot select a model or gear.

Built-ins are `general`, strictly read-only `explore`, and Work-only `work`.
Children cannot ask operator questions, delegate, or use background shell
tools. They inherit the parent's approval and directory boundaries. Custom
tool lists can only narrow the parent catalog. Children share the workspace;
parallel writers must be given non-overlapping work. Separate working copies,
background Agent completion notifications, and direct operator conversations
with a child are not implemented.

Children start with their own role prompt, applicable workspace instructions,
and the delegated task, without parent history or pasted images. Their model,
thinking, context window, service tier, and speed are resolved at creation
and retained when continued. Without a configured model they inherit the
parent's current choices. Explicit models must exist and have credentials;
an unavailable Agent is omitted with an English diagnostic. Per-model options
without an explicit model are ignored. Invalid gears or slots are diagnosed
and fall back without disabling the Agent.

A child run has no model-call, tool-call, or duration limit of its own by
default, for any Agent. It draws on the parent's shared execution budget, so a
long-running child stops only when that budget is spent. `agents.limits` in
`config.json` may set `maximumModelCalls`, `maximumToolCalls`, and
`maximumDurationSeconds` for each child run. A null or omitted field is
unlimited. A child that reaches its own limit stops and reports it in the
`task` result; the parent turn continues.

`agents.maxConcurrent` defaults to 4 and accepts 1 through 8. It limits active
child invocations, including multiple instances of the same Agent. Adjacent
`task` calls may overlap; other tools are serial execution barriers. Parent
cancellation stops running and queued children. Approval interactions are
queued and identify the source child. Approval notifications update only their
originating conversation; opening an approval modal does not commit another
conversation's stream or change its progress. Headless calls requiring an
operator are denied by the same parent policy.

`/agents` lists definitions and settings. While idle it accepts `on`, `off`,
`concurrency <1-8>`, `limit <model-calls|tool-calls|duration> [value]`,
`model <name> [provider model]`, and
`thinking|context|tier|speed <name> [value]`. Omitting the value clears that
choice. Model-specific completions use the Agent's model. Built-in settings
are saved to `config.json`; custom settings go to the corresponding manifest.
`--agents on|off`, `--agents-concurrency <n>`, `--agents-model-calls <n>`,
`--agents-tool-calls <n>`, and `--agents-duration <seconds>` affect only the
process. `limit <model-calls|tool-calls|duration> [value]` is the idle
`/agents` form of `agents.limits`; omitting the value clears that field.

Custom definitions live in `~/.crystal/agents/<name>/` and
`<workspace>/.crystal/agents/<name>/`, containing `agent.json` and `prompt.md`.
`description` is required; `enabled` defaults false. A workspace entry replaces
the entire Home entry, including when disabled or invalid. Built-in names are
reserved. Built-in `general`, `work`, and `plan` prompts are separate from the
parent Work and Plan prompts. Their files are `agents/general`, `agents/work`,
and `agents/plan` under `prompts/`, a selected prompt set, and prompt
attachments. `general` uses `agents/general`, and `agents/plan` when the parent turn is
Plan. `work` uses `agents/work`; it is not offered during a parent Plan turn.
`explore` always uses `agents/plan`. The same precedence applies: built-in text, the selected prompt
set, Home `prompts/agents/`, then the project `prompts/agents/`. Attachments
append only the matching `agents/` file. Custom Agent prompts are not affected
by those overlays, sets, or attachments. They keep `prompt.md`, then the
built-in child safety rules and bound placeholders.

`/child` opens a filterable child picker, `/child <unique-id-prefix>` inspects
one child, and `/child parent` returns. The picker refreshes status and usage
while open and retains selection by child id when active children reorder.
F7 opens the picker from either view;
F7 and Esc in the child picker dismiss it. Inspection works during a turn and
is read-only: child text input, paste, and submit are ignored, while the
parent's composer draft and image attachments remain untouched. Esc returns to the parent at any
draft state when no modal owns the key. Child identity and navigation hints
replace the composer in the read-only view chrome, including the short child id
and Esc/F7 hints, not duplicate transcript rows. F7 and `/child`
are unavailable after Agents are disabled. Child chrome uses the child's model
and usage; its todo panel updates after each child tool batch without waiting
for task completion. Other child streams do not mix into it. Saved-session lists continue
to show only parents.

Children retain their archive and compacted model context, model choices,
usage, todos, and image references inside the parent session file. Resume and
fork carry these records without adding their usage again; clear drops them.
Interrupted processes do not restart child tasks automatically. Continuing a
child rechecks current mode and directory boundaries and cannot widen the
originating parent turn's tool ceiling. Token totals include child model and
compaction calls, using provider-reported usage only. Context estimates stay
separate and are shown with `~`. A text child has no `view_image`; an
image-capable child's tool images go only into that child. `describe_image`
remains a tool-free side request. Child images use the existing media store
and JSON export inlines available bytes.

## Deferred product work

The same items are bucketed in [TODO.md](TODO.md). That file does not add
scope and does not authorize implementation. When an item moves between
buckets, update `TODO.md` in the same change. The constraints below remain
authoritative. Nothing in a priority bucket is scheduled.

The following capabilities are part of the product direction but are not yet
implemented in the current build.

### High priority

- Publishing a NuGet package. Package id, which assemblies ship, and whether
  a library is also published are not defined. Do not reserve a package id or
  add a project. This item is not permission to add or update a package
  reference. Current release artifacts and install scripts remain the install
  path.
- Refactoring and optimizing `crystal run`. It remains the headless entry for
  one task in this executable, not an operator surface and not a second
  assembly. The work must not put secrets on the command line, persist flags
  to `config.json`, or make the run ask the operator. Current flags and exit
  behavior stand until a specific change defines otherwise.
- Image generation. The direction is to let the session produce images,
  whether through a tool or as native model output. Which path comes first,
  which providers and protocols, how a model declares it, how a generated
  image is stored, replayed, resumed, forked, and exported, and how approval
  and cost are handled are not defined. Generated images must keep the
  existing image boundary: signature check, size limit, owner-only media
  store, and `[Image #N]` markers, with no bytes in logs or composer text. Do
  not reserve a configuration field, command, public type, event, wire field,
  or tool-set format. This does not authorize changes under `../Crystal`, and
  approval, trust, the workspace fence, and `ToolInvocationPolicy` stay as
  they are. Current behavior stands: the built-in adapters emit only text,
  reasoning, and tool calls.
- A built-in web search tool. The direction is a host-executed tool that lets
  the model search the web and read the results. The search backend, whether
  a separate page fetch tool exists, how approval and network access are
  classified, result size bounds, and failure behavior are not defined. It is
  not a provider's server-side web search, not an MCP server, and not a
  plugin or tool set. Results are untrusted data, not instructions. Do not
  reserve a configuration field, command, public type, or backend name, and
  do not store a search key in source, logs, or on a command line. Approval,
  trust, the workspace fence, and `ToolInvocationPolicy` stay as they are.
- Session topic. The direction is a short topic for a session that names
  what it is about. `topic.md` (`TopicNamingPrompt`) is a named prompt today,
  but nothing calls it, so no session has a topic. Who sets it (the model
  after the first turn, the operator, or both), which model generates it and
  what that costs, where it is saved, and how an older session without one
  reads are not defined. Do not reserve a configuration field, command,
  public type, event, wire field, or file format. Approval, trust, and
  `ToolInvocationPolicy` stay as they are. Current behavior stands: a session
  has an id and no topic.
- A `topic` custom status-line field. The direction is to show the session
  topic on the custom status line. It depends on the session topic item above
  and is not defined before it. Do not add the field name until the topic
  exists.

### Medium priority

- Automatically fetching model-list information from a provider. `/model`
  still selects only models listed in the configured catalog.
- A full code review of the whole codebase. It examines existing code; it
  does not change product scope, reserve a public type, command, or
  configuration field, or authorize new behavior. Scope, depth, and how
  findings are tracked are not defined. Findings stay inside the boundaries
  in this file and `ARCHITECTURE.md`. It is not the Review or Audit approval
  mode, and it does not change those modes.
- Child Agent configuration. Current `/agents` settings, the `agents` object
  in `config.json`, process `--agents` and `--agents-concurrency`, and custom
  `agent.json` manifests stand. Which settings change, and at which scope,
  are not defined. Do not reserve a new configuration field or command. This
  is not a new Agent, not a mode, and not separate working copies or a direct
  operator conversation with a child.
- Custom-format tools beyond the one built-in case. A model that declares
  `textTools` is offered `apply_patch` as a free-form custom tool, so it
  writes the patch directly instead of wrapping it in JSON (see Data). That
  is the only tool and the only protocol, the Responses protocol, that does
  this. Other free-form tools, other protocols, tools that take more than one
  string, and operator or plugin tools that take free-form text are not
  defined. Do not reserve a command, public type, or tool-set format for
  them. This item does not authorize changes under `../Crystal`. Provider
  adapters still implement only Crystal chat contracts and do not own tools.
- Built-in tool optimization. Improving how the built-in tools behave and
  perform. Which tools and which improvements are not defined. It is not a
  new tool, not operator configuration of tool limits (that question is under
  Pending evaluation below), and not a change to approval, trust, or the
  workspace fence. Current tool behavior stands until a specific change
  defines otherwise. Do not reserve a configuration field, command, or public
  type.
- Bridge model choices in editors that flatten groups. `crystal bridge`
  sends the `model` config option (and the two approval model options) as
  ACP select groups, one per provider, with `provider/model` as the value
  and the bare model name as the label. An editor that does not show group
  headers, as JetBrains was observed not to, lists models from different
  providers together, and the same model name can appear once per provider
  with nothing to tell the entries apart. The direction is a label that
  names the provider when more than one provider is configured, while
  keeping the groups for editors that render them. Whether the label is
  `provider / model`, how an editor that renders groups avoids showing the
  provider twice, and which options it applies to are not defined. Current
  behavior stands until a specific change defines otherwise. It is a change
  to the Bridge wire only: values, slash commands, and the engine option
  catalog stay as they are. Do not reserve a configuration field or public
  type.

### Low priority

- .NET CLI tool support. The direction is to ship the executable as a
  `dotnet tool` so that `dotnet tool install` places it on `PATH` with an
  installed command name. Package id, target frameworks, and packed assemblies
  are not defined. Do not reserve a package id, project, command, or package
  reference. The installed tool remains this executable, not a second
  assembly, and `crystal run` keeps its current entry semantics.
- Other packaging support beyond the self-contained archives and install
  scripts. Channels are not selected, and ownership, signing, update behavior,
  and platform coverage are not defined. Do not reserve a package name, tap,
  formula, image, or repository. Release artifacts and install scripts remain
  the install path until a specific change defines otherwise.

### Deferred

- an operating-system sandbox;
- provider protocols other than DeepSeek and OpenAI-compatible Chat
  Completions, OpenAI Responses, Anthropic Messages, Gemini GenerateContent,
  and Ollama Chat;
- accepting, persisting, or sending audio or video input, and audio or video
  model output. Declaring `audioInput` or `videoInput` does not implement that
  path, and neither does configuring `audioModel` or `videoModel`: those
  fields are reserved so the description path has its settings when it
  arrives. Image model output is High priority (Image generation);
- editing provider definitions in the TUI;
- workspace-level configuration, read from `<workspace>/.crystal/config.json`
  once its field scope, precedence, write policy, and trust semantics are
  defined;
- a module system below plugins and raw hooks, reached through reflection
  or another mechanism, once its behavior and ownership are defined;
- more plugin capabilities beyond tools, built-in replacement, protocol
  clients, classifiers, slash commands, process commands, hooks, raw hooks,
  prompt placeholders, status fields, catalog snapshots, model facts, model
  clients, and data directories. An example is custom tool execution modes. The set is not closed. Behavior and ownership are not
  defined. Do not reserve a command, flag, configuration field, or public
  type;
- switchable thinking-chain compression. After each thinking pass, a model
  compresses and summarizes that thinking according to a requirement. The
  switch, the requirement, which model writes the summary, and whether the
  original thinking remains in the transcript, the archive, and later
  requests are not defined. Do not reserve a prompt file, configuration
  field, or command for it. Context compaction, microcompaction, hiding
  Thinking panels, and a provider's summarized thinking display are not
  this path;
- configurable serial or concurrent execution of ordinary tool-call batches.
  Ordinary Work and Plan tools remain serial, including image-capable turns.
  Adjacent child `task` calls have their own bounded concurrency. A general
  concurrency setting, its default and scope, and ordering for unrelated
  side effects are not defined. Do not reserve a configuration field or
  command for general tool concurrency;
- OAuth sign-in for model providers, such as a ChatGPT account login for
  OpenAI. A provider authenticates only with a static API key today. Which
  providers, which endpoints and client identity, where tokens are stored,
  refresh and concurrent-refresh behavior, how a front end shows the login,
  and whether the terms of the service allow it are not defined. A sign-in
  would have to keep adapters free of `~/.crystal` layout and prompts, keep
  the engine off the console, never put tokens in source, logs, diagnostics,
  or on a command line, and make `crystal run` fail instead of asking the
  operator. Do not reserve a command, configuration field, or public type. It
  is not OAuth for remote MCP servers;
- several concurrent tasks in one terminal process. The terminal drives one
  session at a time; child Agents are not this path. Do not reserve a
  command, configuration field, or public type;
- several active sessions in one `crystal server` process. One process hosts
  one session, and a front end that wants more starts more processes. Plugin
  and external tool load contexts are not collectible, so per-session loading
  in one long-lived process would only grow. Do not reserve a route, flag,
  or public type;

TODO: add image generation only after its path, providers, declaration,
storage and replay, and approval and cost behavior are defined. Do not reserve
a configuration field, command, public type, event, wire field, or tool-set
format.

TODO: add audio/video input and audio or video model output only after their terminal
interaction, persistence, size, and provider semantics are defined. A stored
`audioInput` or `videoInput` declaration is not that path. ACP (Bridge)
stays the front end described in docs/bridge.md. Do not put that protocol in
the engine and do not bypass approval, trust, or the tool boundary. ACP does
not speak MCP itself. Editor-supplied MCP servers use the same MCP client as
local servers.

TODO: add workspace-level configuration only after its field scope, precedence,
write policy, and trust semantics are defined; a repository-controlled file
must not weaken approval or trust.

TODO: add further plugin capabilities only after each capability's behavior
and ownership are defined. A plugin process command is not an in-session
slash command and not `crystal plugins` or `crystal tools`. It must not put
secrets on the command line, must not persist its flags to `config.json`,
and must not make `crystal run` ask the operator. A custom tool execution mode is not the deferred host setting for
serial or concurrent ordinary tool batches. Ordinary Work and Plan tools
remain serial. A plugin must not bypass approval, trust, the workspace
fence, or `ToolInvocationPolicy`. This item is not the deferred module
system and not the undecided extra hook methods or plugin dependencies.

TODO: publish a NuGet package only after its package id, packed assemblies,
and whether a library is also published are defined. Do not reserve a package
id or add a project. This is not permission to add or update a package
reference.

TODO: ship a .NET CLI tool only after its package id, target frameworks, and
packed assemblies are defined. Do not reserve a package id, project, command,
or package reference. The installed tool is not a second assembly, and
`crystal run` keeps its current entry semantics.

TODO: add packaging channels beyond the self-contained archives and install
scripts only after the channels, ownership, signing, update behavior, and
platform coverage are defined. Do not reserve a package name, tap, formula,
image, or repository.

TODO: refactor and optimize `crystal run` without making it an operator
surface, a second assembly, or a place for secrets. Do not persist its flags
to `config.json`, and do not make it ask the operator. Current flags and
exit behavior stand until a specific change defines otherwise.

TODO: add switchable thinking-chain compression only after its switch,
requirement, model choice, and the fate of the original thinking are defined.
It runs after a thinking pass and summarizes that thinking. It is not context
compaction, microcompaction, `verboseThinking`, or `thinkingDisplay`.

TODO: add configurable serial or concurrent execution of ordinary tools only
once its default, scope, and ordering of approval, progress, grants, and side
effects are defined. Ordinary tools remain serial; adjacent child task groups
have the bounded concurrency described above.

TODO: fetch model-list information from a provider only after the request,
failure, and catalog-write behavior are defined. `/model` still selects only
configured models.

TODO: change child Agent configuration only after the settings and their
scope are defined. Current `/agents` settings, the `agents` object, process
overrides, and custom `agent.json` manifests stand. Do not reserve a new
configuration field or command.

TODO: add further free-form text tools only after the participating protocols
and tools and their approval, path classification, and error behavior are
defined. Only `apply_patch` on the Responses protocol, behind a model's
`textTools` declaration, exists today. Do not reserve a command, public type,
or tool-set format. This does not authorize changes under `../Crystal`, and
provider adapters still own no tools.

TODO: add a built-in web search tool only after its backend, approval and
network classification, result bounds, and failure behavior are defined. It is
not a provider's server-side web search. Search results are untrusted data and
must not be followed as instructions. Do not reserve a configuration field,
command, public type, or backend name.

TODO: optimize the built-in tools only after the affected tools and the
intended improvements are defined. Approval, trust, the workspace fence, and
`ToolInvocationPolicy` stay as they are. This is not the undecided question of
operator-configurable tool limits.

They were deferred until the product had a concrete use case and enough runtime
infrastructure to support them safely; they are not excluded from the product.
Implement each capability only when its behavior and ownership are defined, and
do not reserve empty public types in advance. A high or medium priority does
not remove that rule.

[TODO.md](TODO.md) lists the items below under Pending evaluation. They are
not a priority. Parent/child Agents do not decide them.

Whether Plan may include bash is under consideration and is not decided.
The current behavior stands: built-in Plan has no edit, write, apply_patch, or bash. No
change is scheduled, and a later decision may revisit it.

Whether built-in tool limits become operator configuration is under
consideration and is not decided. The current behavior stands: result size
caps, read, grep, glob, search, list, and tree limits, background task counts and
log bounds, the image size limit, and the todo caps are host-owned constants.
Timeouts and turn budgets are already configurable and are not part of this
question. No change is scheduled, and a later decision may revisit it. Such a
decision must define which limits may vary, at which scope, and how a larger
limit interacts with context compaction.

Deferred questions are under consideration and are not decided. The idea is
that the model can ask a low-stakes question without stopping, continue on a
stated default, and have the operator answer later. The current behavior
stands: the built-in `question` tool blocks until the operator answers or
dismisses, and any such path would sit beside it, not replace it. Whether it
exists, which tool carries it, how and when an answer reaches the model, what
happens to unanswered questions when a turn ends, whether Plan gets it, and
how Server, Bridge, child Agents, and `crystal run` treat it are not defined.
Child Agents and `crystal run` would not be able to ask the operator, as
today. No change is scheduled. Do not reserve a tool name, command, public
type, event, or wire field.

Date and time status line fields are under consideration and are not
decided. The status line repaints on session events and the progress spinner,
not on a timer, so a clock field would stop updating while the session is
idle. Whether the fields exist, the repaint mechanism, whether the user sets
the format and the time zone, and how a theme colors them are not defined.
The custom status line has no date or time field today, and `elapsed` is the
time spent on the current turn, not the wall clock. No change is scheduled.
Do not reserve a field name, configuration field, or gauge name.

More hook methods will be added later on the existing hook interfaces, and
are not reserved as empty types in advance. Whether plugins declare
dependencies on one another, and related loading questions, are under
consideration and are not decided. They are not part of the deferred list
above until a decision is made.

## Relationship to Crystal

Crystal is provider-neutral, prompt-neutral, and tool-neutral. It does not
select a model, write a system prompt, ship a filesystem tool, compress
context, or draw a UI.

Crystal.Harness is a named-Agent composition runtime with shared budgets. It
is not this product. This product is named CrystalCode because it is the
coding product built on Crystal. Its Chinese name is "晶码".

## Data

User data lives in `~/.crystal`. Operator preferences load from
`~/.crystal/config.json` only; a workspace-level `config.json` is not read.
Prompts may be replaced in
`~/.crystal/prompts` and the project's `.crystal/prompts`. Provider and model
definitions live in `~/.crystal/providers.json` and in `.json` files under
`~/.crystal/providers/` and its first-level subdirectories; changing
preferences in `config.json` does not rewrite those definitions. A
subdirectory only groups files for the operator and has no runtime meaning.
Provider files are Home-only: a workspace `.crystal/providers` is not read,
and no repository-controlled file can define a provider. One provider name may
appear in only one file. The legacy `config.json.providers` field remains
readable for existing installations that have no provider file.
Home-only reusable prompt sets live under `~/.crystal/promptsets`. Prompt
attachments live under `~/.crystal/prompt-attachments` and
`<workspace>/.crystal/prompt-attachments`. Each directory carries its own
`prompt.json` with an optional display name, an optional description, and
`enabled`. A missing `enabled` value means off. At most one prompt set is
enabled. Attachments may enable many, ordered by `order`. A directory without
a readable manifest is skipped. There is no migration from older
`config.json` keys: `promptSet` and `promptAttachments` are ignored and
omitted the next time preferences are saved. Workspace
hints remain independent and are appended from `instructions.md`, `.crystal.md`, and
OpenCode-compatible `AGENTS.md`, `CLAUDE.md`, and `CONTEXT.md` files. Those rule files
are never prompt overlays. `config.json` `ruleFiles` turns the compatible files
on or off independently for global and project discovery. Both default on, and
the object is omitted until one is off. `/rules` shows or sets them and writes
`config.json`. A process `--rules` value overrides that for one run and is not
written. Crystal `instructions.md` and `.crystal.md` stay on. Skills are discovered from Crystal,
OpenCode, Claude, and Agents skill directories and loaded through the
`skill` tool when enabled. Operator tool sets live under `tools/` in
the home and project `.crystal` trees and are loaded as extra `ITool`
or `IMultimodalTool` entries when External Tools is enabled. A dotnet
tool may also implement `CrystalCode.Tools.IHostTool` or
`IHostMultimodalTool` and then receives the workspace root, session id,
approval mode, and its global and project runtime-data directories on each
call. A disk plugin that implements
`CrystalCode.Plugins.Data.IPluginDataDirectory` receives the same two data
directories. Plugins and tool sets keep that runtime data under
`data/plugins/<directory>/` and `data/tools/<directory>/` in the home and
project `.crystal` trees; the host creates those directories lazily, and they
are separate from the `plugins/` and `tools/` install trees. Tools that
implement only Crystal's
interfaces stay unchanged. Native
multimodal entries are exposed only while the selected model and provider
support image input. Declared `audioInput` or `videoInput` does not expose
them. The resource description models are the image, audio, and video
models (`imageModel`, `audioModel`, `videoModel`). Each has its own switch,
provider and model, thinking gear, service tier, speed, and context budget,
like the approval and compaction models, and `/describe` configures them.
`describe_image` is available on Plan and Work while `imageModel` is on, and
returns a text description from that model. Audio and video description are
not connected: their models can be configured and are validated against
`audioInput` and `videoInput`, but no tool sends audio or video to them. Each side model, and each child Agent with
its own model, is configured on its own: an unset tier or speed is not sent,
an unset context is that model's first declared budget, and the session
`/tier`, `/speed`, and `/context` choice never applies to it. Optional `compactionModel` writes
summaries when enabled; omitted or off, summaries use the session model. `view_image` is available only while
the selected model and provider support image input, and returns the image
itself. A model entry on a `responses` provider may declare `textTools`
(default off). While it is on, `apply_patch` is offered to that model as a
free-form custom tool: the model writes the patch as plain text, the adapter
wraps it as the tool's `patchText` argument, and the host runs, classifies,
approves, and records it exactly as it does the JSON form. The declaration is
refused on any other protocol. Whether each source's author
approval declarations take effect is stored under `externalToolApproval`
in `config.json`. The application never writes
secrets into the workspace.

## Runtime language

All runtime text is English: UI, logs, exceptions, tool outputs authored by
this product, and approval copy.
