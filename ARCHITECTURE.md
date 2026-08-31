# Architecture

## Status

This document is the authoritative architecture for the current coding
product. Public names may change while the product has no compatibility
baseline.

## Dependency direction

```text
CrystalHarness
    ↓
CrystalHarness.Display
    ↓
Spectre.Console   (rasterization; Terminal.Gui is referenced, not called)

CrystalHarness
    ↓
CrystalHarness.Providers
    ↓
Crystal

CrystalHarness also references Crystal.Tools, Crystal.Agents, and
Crystal.Harness directly. CrystalHarness.Display references Spectre.Console
and Terminal.Gui only. It does not reference Crystal, Crystal.Tools, or
the executable host. CrystalHarness.Providers references only Crystal.
No project in this repository modifies Crystal.
```

CrystalHarness.Tests references CrystalHarness.
CrystalHarness.Display.Tests references CrystalHarness.Display.
CrystalHarness.Providers.Tests references CrystalHarness.Providers.

## Assembly ownership

### CrystalHarness

The executable host. Owns CLI commands, session and turn execution,
Plan/Work catalogs, approval policy, context compaction, `~/.crystal`
storage, built-in coding tools, prompts, and in-process plugin contracts.
It projects session state onto CrystalHarness.Display. It does not own
the frame painter, composer buffer, or transcript log.

### CrystalHarness.Display

The terminal UI library. Owns the alternate-screen frame, input routing,
composer, transcript viewport, markdown paint, and queue card. Spectre
widgets are rasterized into frame rows. `AnsiConsole.Live` is not the
session shell. Terminal.Gui is a parked package reference for
supply-chain review and is not called. The host loop stays in this
assembly's shell types; a later migration to Terminal.Gui would replace
that loop inside this project only.

### CrystalHarness.Display.Tests

Display tests: layout, chrome, scroll input, composer, paint, transcript
log, and queue card. Does not reference the host executable.

### CrystalHarness.Providers

Model adapters. Each adapter implements `IChatClient` and, when the provider
can stream, `IStreamingChatClient`. Provider options, wire DTOs, and
transport exceptions stay in this assembly. No tools, no UI, no home-directory
layout.

### CrystalHarness.Tests

Host tests: workspace fencing, approval classification, compaction
selection, session serialization, and command behavior.

### CrystalHarness.Providers.Tests

Provider adapter tests. One folder per vendor, mirroring
`CrystalHarness.Providers`. Does not reference the host executable.

## Namespace ownership

Root identifier is `CrystalHarness`, matching the existing solution. Folder
path under a project root equals the namespace after the project name.

CrystalHarness (executable):

| Folder | Owns |
| :--- | :--- |
| `Commands` | Spectre.Console.Cli commands |
| `Configuration` | Loaded options, defaults, thinking chrome labels |
| `Home` | `~/.crystal` paths and file I/O |
| `Sessions` | Transcript, ledger, streaming turn, slash commands, session renderer, replay, tool-call text, usage text, question prompt |
| `Approvals` | Risk, authority, grants, policy, review transcript, approval cards and keys |
| `Approvals/Interfaces` | Prompt and reviewer contracts |
| `Compaction` | Window accounting and summary substitution |
| `Tools` | Workspace fence and built-in `ITool` types |
| `Prompts` | Caller-owned system text. Built-in Work and Plan identify the assistant as Crystal Code |
| `Plugins` | In-process registry and built-in contributions |
| `Plugins/Interfaces` | Contribution contracts |
| `Plugins/Providers` | Built-in DeepSeek and OpenAI client factories |

CrystalHarness.Display:

| Folder | Owns |
| :--- | :--- |
| `Shell` | Alternate screen, painter, layout, key burst, scroll input, status chrome |
| `Composer` | Prompt buffer, keys, slash picker |
| `Cards` | Queue overlay |
| `Transcript` | Viewport log, role cards, sequential fallback |
| `Paint` | Markup, markdown, theme, wrapping |

Provider types live under `CrystalHarness.Providers` plus one folder per
vendor (`DeepSeek`, `OpenAI`). Shared OpenAI-compatible request and stream
parsing lives in `Compatible` so neither vendor adapter reimplements the
wire format.

## Crystal consumption

The interactive turn uses `IStreamingChatClient` and `ToolExecutor`. It does
not use `Crystal.Agents.Agent` for the live UI: that Agent completes model
turns without token streaming.

The product does not use `Crystal.Harness.AgentHarness` until it has a real
parent/child Agent topology.

Approval is a `ToolInvocationPolicy` supplied to `ToolExecutor`. Rejection
returns Harness-authored `ToolOutput`. Tool exceptions become model-visible
only through a Harness `ToolExceptionMapper`.

## Session and turn

One user message is one turn:

1. Snapshot the transcript and current tool definitions.
2. Stream one chat request. Render deltas as they arrive.
3. Select candidate zero.
4. If the candidate has tool calls, execute the full batch through
   `ToolExecutor` (approval runs first).
5. Append exact `ToolResult` values.
6. Repeat until the candidate has no tool calls, a configured limit stops
   the turn, or the user cancels. Before each model round, compact if the
   estimated request is over budget. One failed compact while still over
   budget stops the turn (`context_overflow`).
7. After a completed turn, consider compaction from reported token usage.
   `/compact` (alias `/summarize`) runs the same summarizer immediately.

The composer stays open while a turn runs. Enter with text enqueues a
follow-up (FIFO). Queued items stay in a panel above the composer until
they are sent. The queue is sent when the current tool batch finishes or
when the turn (thinking or conversation) ends. Empty Enter still
interrupts immediately and sends. Interrupt (Ctrl+C or empty Enter) does
not drop queued text. At an idle prompt, Ctrl+C clears the composer.
Two Ctrl+C presses on an empty composer exit.

## Plan and Work

These are product modes, not Crystal types.

- Plan registers read, glob, grep, todowrite, and question.
- Work registers those tools plus edit, write, and bash.

Switching modes replaces the first system message and the executor catalog.
The transcript is otherwise the same conversation.

Live Work and Plan system text is assembled in this order: the overlayable
Work or Plan body, a host-owned `<env>` block (workspace path, whether the
directory is a git repo, platform, today's date, and provider/model), then
Workspace instructions. Review is the named file alone. The env block is
not an overlay file and is refreshed on `/cd` and when the live system
message is replaced.

## Approval

Every side-effect tool call is classified before invocation:

- Risk: Read, Write, Privileged, Forbidden.
- Authority: Workspace, OutsideWorkspace, Network, PrivilegedEscalation.
- Grant: Once, Session, Persistent.

Modes:

- Plan: read-only catalog. No edit, write, or bash.
- Default: Read auto-executes. Write and shell ask the operator.
- AutoEdit: workspace file changes pass without review. Shell still asks.
- Review: another model checks each remaining side-effect call (Codex
  guardian-style). A bounded transcript excerpt is attached: the first
  and latest user turns as authorization anchors, other user turns that
  fit, then recent assistant and tool evidence. A compaction summary
  stands in for folded user turns. Without that evidence the host asks
  the operator. Later user messages refine the task; a status question
  does not revoke earlier authorization. The reviewer returns `outcome`
  (allow / deny / ask), `risk_level` (low / medium / high),
  `user_authorization` (low / medium / high), and `rationale`. Allow
  executes. Deny becomes model-visible rejection text. Ask and
  Forbidden-allow fall back to the operator. Review is not a grant and
  is not full pass-through.
- Full: workspace-bounded, policy-allowed actions pass without review.
  Forbidden and Privileged never fully auto-pass.

Do not name a mode `auto`. That word is ambiguous between review and
full pass-through.

When a call auto-passes (policy, remembered grant, or review allow),
the shell prints a panel with Title Case fields: Status, Reason, Risk,
and Authority, plus the classifier summary. A review allow also prints
Outcome, review Risk, Authority, and rationale.

Persistent grants are stored in `~/.crystal/permissions.json`.

## Compaction

Crystal does not reduce context. When estimated or reported tokens cross
the configured fraction of the selected model's usable window (context
minus reserved output), the host:

1. Clears old tool results outside a protected recent band, when enough
   tokens would be freed.
2. Asks the model for one structured summary of older turns, folding any
   previous summary, and keeps a recent tail verbatim.
3. Stops if the summary request itself cannot fit or the summarizer
   returns nothing and prune did not help. The turn does not retry
   compaction in a loop.

`/compact` (alias `/summarize`) runs that path immediately. It is
refused while a turn is running. User and assistant text in the folded
head are replaced by the summary; they are not kept beside it.

Sessions are written to `~/.crystal/sessions/<id>.json` after each
completed turn, after a successful `/compact`, and on an orderly exit
when the transcript has a user message or a compaction summary. The
file stores the compacted model transcript (live system prompt, one
summary, recent tail) plus the last usage snapshot. `crystal --resume
<id>` (`-r`) loads that file at process start; a missing or empty
session exits without entering the TTY. `/resume` restores the same
transcript from inside a running session: the live system prompt is
refreshed from current Plan/Work text; the summary and tail are kept.
Usage is restored so the status bar and the next compact decision have
a baseline. `/clear` starts a new id.

## Home directory

```text
~/.crystal/
  config.json
  credentials.json
  permissions.json
  instructions.md
  prompts/work.md
  prompts/plan.md
  prompts/review.md
  sessions/<id>.json
  logs/
  plugins/
```

Project overlay (wins over home for named prompts):

```text
<workspace>/.crystal/
  instructions.md
  prompts/work.md
  prompts/plan.md
  prompts/review.md
<workspace>/.crystal.md
```

Overlay is built-in default, then `~/.crystal`, then the project
`.crystal`. Named prompt files replace the built-in Work, Plan, or
Review system text.

Built-in Work and Plan identify the assistant as Crystal Code. Operators
who replace those files choose their own identity.

`instructions.md` and `.crystal.md` are appended under
"Workspace instructions" on Work and Plan only. Review is the named
file alone so the reviewer stays a safety check. Files may be `.md`
or `.txt`. Empty files are treated as missing. The host never writes
prompt files. The host appends a non-overlayable `<env>` block between
the Work or Plan body and those instructions.

`AGENTS.md` and `CLAUDE.md` are OpenCode-compatible rule files, not
prompt overlays. They are combined into the same instruction block
and never replace Work, Plan, or Review.

- Global: first existing file among `~/.crystal/AGENTS.md`,
  `~/.crystal/CLAUDE.md`, `~/.config/opencode/AGENTS.md`, and
  `~/.claude/CLAUDE.md`.
- Project: walk from the workspace up to the git root. The first
  matching name wins (`AGENTS.md`, then `CLAUDE.md`, then
  `CONTEXT.md`). Every file of that name on the walk is appended.
  `CLAUDE.md` is used only when no `AGENTS.md` exists on the walk.

`credentials.json` is created with owner-only permissions and is keyed by
provider name. Environment variables override file credentials. The `plugins`
directory is reserved for later assembly loading; the current product only
registers in-process contributions.

Provider names are open. `deepseek` and `openai` are starter entries. A user
adds an OpenAI-compatible endpoint by inserting another `providers` object
with `protocol` `openai`, a `baseUri`, and a `models` table. Context size
and sampling live on each model, not on the host. Thinking capability
also lives on the model. The current thinking gear is a host setting.

```json
{
  "provider": "openrouter",
  "model": "anthropic/claude-sonnet-4",
  "thinkingEffort": "high",
  "providers": {
    "openrouter": {
      "protocol": "openai",
      "baseUri": "https://openrouter.ai/api/v1/",
      "replayReasoningContent": true,
      "tokenLimit": "max_tokens",
      "apiKey": "sk-or-...",
      "apiKeyEnvironment": "OPENROUTER_API_KEY",
      "models": {
        "anthropic/claude-sonnet-4": {
          "contextWindow": 200000,
          "temperature": 0.2,
          "maxTokens": 8192,
          "thinking": true,
          "thinkingEfforts": ["low", "medium", "high"]
        }
      }
    }
  }
}
```

`thinking` and `thinkingEfforts` declare whether the model supports
thinking and which Crystal effort names it accepts (`minimal`, `low`,
`medium`, `high`, `maximum`). `max` is accepted as `maximum`. Built-in
DeepSeek V4 models enable thinking with `low`, `high`, and `maximum`. An empty `thinkingEfforts`
list is on/off only.

`thinkingEffort` is the operator choice: `default`, `off` (`none` is
the same), or a Crystal effort name. It is not stored on the model. `/thinking` (alias
`/think`) cycles the gear or sets one by name. Changing models never
fails: if the model does not support thinking, requests omit reasoning
hints; if the stored gear is not in that model's list, the request
uses the provider default and the stored choice is unchanged.

`protocol` is `deepseek` or `openai`. Models that are not listed cannot be
selected. There is no global context window.

`apiKey` may be a literal secret, `{env:NAME}`, or `{file:path}` (relative
to `~/.crystal` or absolute, with `~` expanded). Process environment
variables still override. `credentials.json` remains a fallback store.

## Display

CrystalHarness.Display is the TUI host. Spectre.Console supplies markup,
color, panels, grids, rules, and padding as an offline rasterizer.
`AnsiConsole.Live` is not the session shell: it fights the composer.
Widgets are rasterized into frame rows. The shell enters the alternate
screen when the terminal is a TTY and paints a retained frame: transcript
viewport, optional overlay, status bar, and multiline composer. Unchanged
rows are left in place; a width or height change clears the buffer. The
executable maps turns onto that frame through `SessionRenderer`; it does
not paint rows itself.

Terminal.Gui is referenced from CrystalHarness.Display with a floating
version and is not used. Do not call `Application.Init` or mix a second
console writer with the self-owned loop.

The status bar shows approval, thinking (when the selected model
supports it: `Think Off`, or `Think` plus the resolved gear when
thinking is on), model, workspace, context percent (`CTX`),
token counts (`IN` / `OUT`), tool count (`Tool` / `Tools`), and
elapsed time. Named chrome labels are Title Case. Short status
abbreviations are uppercase. Mode is Plan or Work on the
composer prompt and is not repeated on the status bar. Assistant text is
rendered as markdown while it streams and after it commits (headings,
lists, fenced code with a dim code background, inline code and bold).
User, thinking, tool, and result blocks are rounded panels. A live turn
writes a Tool card when the model round closes with tool calls, before
those calls execute, using the same one-line summary as session replay.
Tool names in chrome and cards are Title Case; stream name chunks are
coalesced so a repeated snapshot does not become `ReadReadRead`.
Approval and questions are Spectre panels with a two-column Title Case
field grid (`Status`, `Reason`, `Risk`, `Authority`, `Outcome`). Ask and
auto-pass cards for `edit` and `write` show a capped `+` / `-` preview
of `old_string` / `new_string` or `contents`. Overlay keys share the
session frame loop, so transcript scroll and resize still work while a
prompt is up. Ask overlays use the same card; `Y` / `S` / `A` / `N` map
Once / Session / Always / Deny. The follow-up queue is a `Queued` panel
above the composer.

Composer keys: Enter submits when idle and queues while a turn is
running. Queued text stays above the composer and is sent when the
current tool batch or turn ends. Empty Enter while working interrupts
immediately and sends. Backspace deletes one character on every
platform. Windows Ctrl+Backspace deletes a word. On Unix, ReadKey tags
plain Backspace as Control; that is still one character. Ctrl+W or
Alt/Option+Backspace deletes a word. Ctrl+C at idle clears the composer.
Two Ctrl+C presses on an empty composer exit. Ctrl+J or `\`+Enter inserts a
newline. Tab toggles Plan/Work or completes a `/` command. Shift+Tab also toggles Plan/Work. Chrome labels are Plan, Work, Review, Default, AutoEdit, and Full.
Status abbreviations are CTX, IN, and OUT. The status
bar includes a queued count while follow-ups wait. Auto-pass prints a
panel with Status, Reason, Risk, Authority, and, for review, Outcome
plus rationale. Reasoning streams into the
transcript. Built-in slash verbs live in `SlashCatalog` and include
aliases (`/new` is `/clear`, `/continue` and `/sessions` are `/resume`,
`/q` and `/exit` are `/quit`, `/think` is `/thinking`, `/summarize` is
`/compact`). A slash picker appears while the prompt
is a command prefix. After a verb that takes a fixed argument
(`/thinking`, `/approval`), Tab also completes the argument. PageUp, PageDown, the mouse wheel, Ctrl+Up/Down,
and Up/Down when the prompt is empty scroll the transcript. Up/Down
arrows navigate composer history or the slash picker when the prompt
has text. The alternate screen enables alternate-scroll arrows (1007)
and bracketed paste (2004). It does not enable SGR mouse tracking
(1000/1006), so left-drag still selects and copies. Wheel reports that
a terminal still sends are drained without waiting. Escape is held only
when no further bytes are available or the sequence is still incomplete.
Paste is the text
between CSI `200~` and `201~`; a printable
key burst is still treated as paste when those markers are absent.
The frame polls terminal size and
repaints when the window is resized. Escape sequences that are not a
bracketed-paste wrap are not treated as paste. Redirected output stays
sequential.

## Plugins

`IPlugin` contributes tools, chat-client factories, approval classifiers, or
slash commands through `PluginContribution`. Built-in tools and the DeepSeek
and OpenAI adapters register through the same table. Disk isolation with
`AssemblyLoadContext` is later work; do not pretend it exists.

Environment variables:

- `CRYSTAL_HOME` overrides the data directory.
- `DEEPSEEK_API_KEY`, `OPENAI_API_KEY`, and `CRYSTAL_API_KEY` override
  `credentials.json`. A provider-specific variable wins over `CRYSTAL_API_KEY`.
