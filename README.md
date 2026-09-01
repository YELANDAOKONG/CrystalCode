# Crystal Code

A production coding TUI for local repositories. The terminal is the
only operator surface. It runs a streaming model-and-tool loop, with
Plan/Work modes, risk-aware approval, automatic context compaction,
and operator data under `~/.crystal`.

CrystalCode consumes the Crystal library. It does not modify Crystal.
It is not a Crystal demo and not a replacement for Crystal.

## What it does

From a workspace you want the agent to inspect or change, CrystalCode:

- streams a model turn with tool calls, and queues follow-ups while a
  turn is running;
- retries a failed model round on rate limits, server errors, timeouts,
  network faults, and incomplete streams, waiting with backoff;
- switches the configured provider and model with `/model`;
- switches Plan (built-in reads; no edit, write, or bash) and Work
  (edit, write, shell);
- approves side effects manually, by a reviewing model, or by full
  pass-through according to risk and authority;
- compact conversation context when usage approaches the selected
  model's window;
- persists configuration, permissions, and sessions under `~/.crystal`;
- talks to DeepSeek and OpenAI-compatible Chat Completions, OpenAI Responses,
  and Anthropic Messages endpoints, including operator-added gateways.

Built-in tools and all provider adapters register through the same in-process
plugin table. Operators add extra catalog tools as
tool sets under `~/.crystal/tools` and `<workspace>/.crystal/tools`.
`IPlugin` assemblies are not loaded from `plugins/`.

## Install

The latest self-contained release can be installed on Linux x64, Linux ARM64,
macOS ARM64, or Windows x64. The installers download the matching standard
release asset, `CrystalCode-<os>-<architecture>.zip`, then replace the
full published contents in `~/.crystal/binaries/code/`.

On Linux or macOS, run:

```bash
curl --fail --location --show-error \
  https://raw.githubusercontent.com/YELANDAOKONG/CrystalCode/master/scripts/install.sh | sh
```

On Windows PowerShell:

```powershell
Invoke-RestMethod `
  -Uri https://raw.githubusercontent.com/YELANDAOKONG/CrystalCode/master/scripts/install.ps1 | Invoke-Expression
```

To inspect an installer before running it, download it to a file and run it
manually instead.

The installers do not write credentials. On Linux and macOS, the installer adds
a `# Crystal Code CLI (Installer)` block to the selected zsh or bash profile.
The block adds the CrystalCode directory to PATH and aliases `crystal` to
`CrystalCode`. On Windows, the installer adds that directory to the user-level
PATH.
Open a new terminal after installation, then run `crystal` on Linux or macOS,
or `CrystalCode` on Windows.

## What it does not do

The current product does not include MCP servers, a headless CI runner,
an operating-system sandbox, parent/child Agents, multimodal coding
(images, audio, video), or provider protocols other than DeepSeek and
OpenAI-compatible Chat Completions, OpenAI Responses, and Anthropic Messages.

## Requirements

- An API key for the selected provider, supplied through configuration
  or the environment (see [Credentials](#credentials))
- A TTY for the interactive alternate-screen UI
- `bash` on the `PATH` (Git Bash is used on Windows when present)

Building from source also requires:

- .NET 10 SDK
- A sibling checkout of Crystal at `../Crystal` (relative to this
  repository root)

## Build

From the repository root:

```bash
dotnet build CrystalCode.sln
dotnet test CrystalCode.sln
```

The executable project is `CrystalCode`. The Spectre application
name is `crystal`.

## Run

Start from the workspace the agent should edit. The current directory
is the workspace unless `--workspace` is set.

```bash
dotnet run --project CrystalCode -- --provider deepseek --model deepseek-v4-flash
```

CLI options:

| Option | Meaning |
| :--- | :--- |
| `-p`, `--provider <name>` | Provider key in `config.json` (`deepseek`, `openai`, or a name you added) |
| `-m`, `--model <id>` | Model id listed under that provider |
| `-w`, `--workspace <path>` | Workspace root (default: current directory) |
| `--home <path>` | Data directory (default: `CRYSTAL_HOME`, then `~/.crystal`) |
| `-r`, `--resume <id>` | Replay that session file under `~/.crystal/sessions` |

`--help` prints the same options.

The first run creates `~/.crystal` (or `--home` / `CRYSTAL_HOME`) and
writes a starter `config.json` if one is missing. Defaults are
provider `deepseek`, model `deepseek-v4-flash`, approval `default`,
and compaction at 80% of the selected model's `contextWindow`.

If the provider has more than one model and neither `config.json` nor
`--model` picks one, the process exits and asks for `--model`.

## Credentials

Do not put secrets in the workspace, in this repository, or in commit
contents. CrystalCode never writes secrets into the project tree.
`credentials.json` is created with owner-only permissions where the
operating system allows it.

Resolution order for the active provider:

1. Process environment (see below)
2. `providers.<name>.apiKey` in `config.json`
3. `~/.crystal/credentials.json`, keyed by provider name

Environment names, in order:

1. `providers.<name>.apiKeyEnvironment` when set
2. `<PROVIDER>_API_KEY` derived from the provider name (hyphens
   become underscores; for example `DEEPSEEK_API_KEY`,
   `OPENAI_API_KEY`, `OPENROUTER_API_KEY`)
3. `CRYSTAL_API_KEY` (shared fallback)

A provider-specific variable wins over `CRYSTAL_API_KEY`.

`providers.<name>.apiKey` may be one of:

| Form | Meaning |
| :--- | :--- |
| `{env:NAME}` | Read the named process environment variable |
| `{file:path}` | Read a file (relative to `~/.crystal`, or absolute; `~` is expanded) |
| a literal string | Used as-is (avoid this in shared files) |

Prefer `{env:NAME}` or `{file:path}` so `config.json` can be copied
without embedding a secret.

`credentials.json` shape:

```json
{
  "deepseek": {
    "apiKey": ""
  }
}
```

Leave the value empty in examples and in any file that might be
shared. Put the real secret only in the local environment or in a
file that is not committed.

If no key is found, the process prints an English error and exits
with status 1. It does not print the secret.

## Configuration

Host settings live in `~/.crystal/config.json`. The first run writes
starter DeepSeek and OpenAI entries. You may edit the file, then
restart.

Top-level fields:

| Field | Meaning |
| :--- | :--- |
| `provider` | Active provider name |
| `model` | Active model id (must exist under that provider) |
| `approval` | `default`, `autoedit`, `review`, `fullreview`, or `full` |
| `thinkingEffort` | Host thinking gear: `default`, `off` (`none` is the same), or a Crystal effort name |
| `skills` | Enable the `skill` tool and available-skill guidance (default `true`) |
| `externalTools` | Enable operator tool set discovery (default `true`) |
| `estimatedTokens` | Show a live four-characters-per-token estimate on the progress row during Thinking and Writing (default `false`) |
| `compactionThreshold` | Fraction of the selected model's `contextWindow` that triggers compaction (greater than 0, at most 1; default `0.8`) |
| `providers` | Named endpoints and their model tables |

There is no global context window. Models that are not listed cannot
be selected.

### Built-in providers

Starter catalog (merged with your `providers` overlay):

| Provider | Protocol | Default base URI | Starter models |
| :--- | :--- | :--- | :--- |
| `deepseek` | `deepseek` | `https://api.deepseek.com/` | `deepseek-v4-flash`, `deepseek-v4-pro` |
| `openai` | `openai` | `https://api.openai.com/v1/` | `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna` |

Built-in DeepSeek V4 models enable thinking with efforts `low`,
`high`, and `maximum`. Each has a 1,000,000-token context window.
Starter OpenAI models use a 400,000-token window and do not enable
thinking unless you add it.

### Provider fields

| Field | Meaning |
| :--- | :--- |
| `protocol` | `deepseek`, `openai`, `responses`, or `anthropic` |
| `baseUri` | Absolute API base URI; the adapter appends `chat/completions`, `responses`, or `messages` |
| `organization` | Optional OpenAI organization for the `openai` protocol |
| `project` | Optional OpenAI project for the `openai` protocol |
| `replayReasoningContent` | Replay provider reasoning content (DeepSeek always does this) |
| `tokenLimit` | Chat Completions output field: `max_tokens` or `max_completion_tokens` (ignored by `responses` and `anthropic`) |
| `apiKeyEnvironment` | Preferred environment variable name for this provider |
| `apiKey` | Literal, `{env:NAME}`, or `{file:path}` |
| `models` | Table of selectable model ids |

Provider names are letters, digits, hyphen, or underscore.

All protocol adapters send the constant `User-Agent: Crystal Code`, with no
version. `responses` authenticates with `Authorization: Bearer`; `anthropic`
uses `x-api-key` and `anthropic-version: 2023-06-01`. Both adapters use direct
HTTP and JSON/SSE handling; no provider SDK is required.

### Model fields

| Field | Meaning |
| :--- | :--- |
| `contextWindow` | Required. Positive token window used for compaction and the status bar |
| `temperature` | Optional, 0 to 2 |
| `topP` | Optional, 0 to 1 |
| `maxTokens` | Optional positive output-token cap |
| `thinking` | Whether the model accepts reasoning hints |
| `thinkingEfforts` | Crystal effort names this model accepts: `minimal`, `low`, `medium`, `high`, `maximum` (`max` is stored as `maximum`) |

`thinkingEffort` is a host setting, not a model field. Changing
models never fails: if the model does not support thinking, requests
omit reasoning hints; if the stored gear is not in that model's list,
the request uses the provider default and the stored choice is
unchanged. An empty `thinkingEfforts` list is on/off only.

### Example: add an OpenAI-compatible provider

Do not put a secret in `apiKey`. Point at an environment variable.

```json
{
  "provider": "openrouter",
  "model": "anthropic/claude-sonnet-4",
  "approval": "default",
  "thinkingEffort": "high",
  "compactionThreshold": 0.8,
  "skills": true,
  "providers": {
    "openrouter": {
      "protocol": "openai",
      "baseUri": "https://openrouter.ai/api/v1/",
      "replayReasoningContent": true,
      "tokenLimit": "max_tokens",
      "apiKey": "{env:OPENROUTER_API_KEY}",
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

Then export `OPENROUTER_API_KEY` in the shell that starts the
process. Restart after editing `config.json` to add providers. CLI
`--provider` and `--model` override the file for that run; `/approval`,
`/thinking`, and `/model` write the new values back to `config.json`.

### Example: add OpenCode Zen protocol endpoints

One OpenCode catalog may be split into multiple provider entries because model
families use different wire protocols:

```json
{
  "providers": {
    "opencode-zen-responses": {
      "protocol": "responses",
      "baseUri": "https://opencode.ai/zen/v1/",
      "apiKey": "{env:OPENCODE_ZEN_API_KEY}",
      "models": {
        "gpt-5.6-sol": {
          "contextWindow": 1050000,
          "maxTokens": 128000,
          "thinking": true,
          "thinkingEfforts": ["low", "medium", "high", "maximum"]
        }
      }
    },
    "opencode-zen-anthropic": {
      "protocol": "anthropic",
      "baseUri": "https://opencode.ai/zen/v1/",
      "apiKey": "{env:OPENCODE_ZEN_API_KEY}",
      "models": {
        "claude-sonnet-5": {
          "contextWindow": 1000000,
          "maxTokens": 128000,
          "thinking": true,
          "thinkingEfforts": ["low", "medium", "high", "maximum"]
        }
      }
    }
  }
}
```

Keep Chat Completions models in a separate `protocol: "openai"` provider entry.

## Interactive session

The default command opens an alternate-screen shell when stdout is a
TTY: transcript viewport, optional overlay, optional progress row,
status bar, and a multiline composer. Redirected output stays sequential.

The status bar shows approval, thinking (when the selected model
supports it), model, workspace, context percent (`CTX`), token counts
(`IN` / `OUT`), a Title Case total when the bar has room, tool count,
and elapsed time. Named chrome labels are
Title Case; short status abbreviations are uppercase. Mode is Plan or
Work on the composer prompt, not repeated on the status bar. A
queued-follow-up count appears while items wait. While a turn runs, a
progress row sits above the status bar (`Awaiting Approval · 5s`,
`Running Command · 2m18s`, `Thinking · 1m16s · ~1.2k Tokens`), prefixed with a spinner, and is
independent of the status-bar activity bullet. The `~N Tokens` estimate
appears only when `estimatedTokens` is on. Session start and `/cd`
show `Loading Tools` on that row while operator tool sets load, after
the frame is already up.

Assistant text is rendered as markdown while it streams and after it
commits (headings, lists, fenced code, inline code and bold). User,
thinking, tool, and result blocks are rounded panels. Tool names in
chrome are Title Case. Approval cards for edit and write show a short
`+` / `-` preview of the change.

### Composer keys

| Key | Action |
| :--- | :--- |
| Enter | Submit when idle; queue a follow-up while a turn is running |
| Empty Enter while working | Interrupt immediately and send the queue |
| Ctrl+J or `\` then Enter | Insert a newline |
| Backspace | Delete one character |
| Ctrl+W or Alt/Option+Backspace | Delete a word (Windows: Ctrl+Backspace) |
| Tab | Toggle Plan/Work, or complete a `/` command (and its argument after `/thinking`, `/approval`, `/model`, or `/tokens`) |
| Shift+Tab | Toggle Plan/Work |
| `?` on an empty composer | Show shortcuts and commands |
| Up / Down | Composer history, or slash-picker navigation when the prompt has text; empty Up/Down scroll the transcript |
| PageUp / PageDown, mouse wheel, Ctrl+Up/Down | Scroll the transcript |
| Ctrl+C during a turn | Cancel the turn |
| Ctrl+C at idle | Clear the composer |
| Ctrl+C twice on an empty composer | Exit |

The alternate screen enables alternate-scroll arrows and bracketed
paste. Mouse tracking stays off so left-drag selects and copies. Wheel
reports that a terminal still sends are drained without waiting. The
frame repaints when the terminal is resized. Escape sequences that are
not a paste wrap are not treated as paste. Overlay prompts (approval
and questions) keep using that same input loop, so scroll and resize
still work while they are open.

Question prompts support one or more tabbed questions, described choices,
single or multiple selection, and custom text answers. Custom input is enabled
by default. A single single-select answer submits immediately; multiple
questions and multiple selection use a Confirm tab. Use Up/Down or `J`/`K` to
move, Enter to select, Space to toggle multiple choices, Left/Right or Tab to
navigate questions, and Escape to dismiss the request. While a custom answer is
being edited, its text and cursor appear inside the question panel; Enter saves
it and Escape returns to the choices without changing the answer. PageUp,
PageDown, the wheel, and Ctrl+Up/Down remain available for transcript scrolling.

### Follow-up queue

The composer stays open while a turn runs. Enter with text enqueues
a follow-up (FIFO). Queued items stay in a `Queued` panel above the
composer. The queue is sent when the current tool batch finishes or
when the turn (thinking or conversation) ends. Interrupt does not
drop queued text.

`/quit`, `/clear`, and `/resume` stop a busy turn before they run.

### One turn

1. Snapshot the transcript and current tool definitions.
2. Stream one chat request. Render deltas as they arrive.
3. If the candidate has tool calls, run the full batch through the
   executor (approval first).
4. Append exact tool results.
5. Repeat until there are no tool calls, a limit stops the turn, or
   you cancel. The host may compact before a model round; if compaction
   cannot reduce further, the turn stops.
6. After a completed turn, consider compaction from reported token
   usage. `/compact` summarizes older context immediately.

## Modes

These are product modes, not Crystal types. Switching replaces the
first system message and the tool catalog. The transcript is
otherwise the same conversation.

| Mode | Tools | Side effects |
| :--- | :--- | :--- |
| **Plan** | Built-in read, glob, grep, todowrite, question, and skill when enabled, plus any external tools listed for Plan | No built-in edit, write, or bash. External Plan tools keep a Write + Workspace floor and still go through approval. |
| **Work** | Built-in Plan tools plus edit, write, bash, plus external tools listed for Work | After approval |

Tab, Shift+Tab, or `/plan` toggles Plan and Work.

## Approval

Every side-effect tool call is classified before invocation.

Risk: Read, Write, Privileged, Forbidden.

Authority: Workspace, OutsideWorkspace, Network, PrivilegedEscalation.

Grant: Once, Session, Persistent.

| Mode | Behavior |
| :--- | :--- |
| **Default** | Workspace read auto-executes. Write, shell, and reads outside the workspace ask you. When Skills is enabled, any path in a Skills search directory auto-passes. |
| **AutoEdit** | Workspace file changes for built-in `write` and `edit` pass without review. Shell, external tools, and outside-workspace reads still ask. |
| **Review** | Workspace file changes for built-in `write` and `edit` pass without review, same names as AutoEdit. Another model checks each remaining side-effect call, including bash, reads outside the workspace, and external Write. Skills search directories auto-pass when Skills is enabled. A bounded transcript excerpt is attached (first and latest user turns as anchors, then other user turns, then recent assistant and tool evidence). A compaction summary stands in for folded turns. Without that evidence the host asks you. Later user messages refine the task; a status question does not revoke earlier authorization. Allow executes. Deny becomes model-visible rejection text. Ask and Forbidden-allow fall back to you. Review is not a grant and is not full pass-through. |
| **FullReview** | The same reviewer and transcript rules as Review, but workspace `write` and `edit` are also checked. They do not auto-pass. |
| **Full** | Workspace-bounded, policy-allowed actions pass without review, including any loaded external tool that stays Write + Workspace. Forbidden, Privileged, and outside-workspace paths never fully auto-pass. |

Do not name a mode `auto`. That word is ambiguous between review and
full pass-through. `/approval` with no argument cycles Default,
AutoEdit, Review, FullReview, and Full. `/approval review` (and the
other names) sets one mode and writes it to `config.json`.

When you are asked, the overlay uses a two-column field grid
(Status, Reason, Risk, Authority, and for review also Outcome plus
rationale):

| Key | Grant |
| :--- | :--- |
| Y, Enter, or 1 | Once |
| S or 2 | Session |
| A or 3 | Always (persistent) |
| N, Escape, or 4 | Deny |

Persistent grants are stored in `~/.crystal/permissions.json`.

When a call auto-passes (policy, remembered grant, or review allow),
the shell prints a panel with Status, Reason, Risk, and Authority,
plus the classifier summary.

Shell classification treats `sudo`, destructive filesystem commands,
pipe-to-shell downloads, force-push, and credential-path writes as
Forbidden or Privileged. Forbidden never fully auto-passes. Review and FullReview
may deny those calls or escalate them to you. Writes under `.ssh`,
`.gnupg`, or `~/.crystal/credentials.json` are Forbidden.

## Thinking

`/thinking` (alias `/think`) cycles the host gear, or sets one by
name: `off`, `none`, `default`, `minimal`, `low`, `medium`, `high`,
`maximum`, `max`. Tab completes the argument from the efforts the
selected model lists. The choice is written to `config.json`.

If the selected model does not support thinking, the command reports
that and does nothing. The status bar shows `Think Off`, or `Think`
plus the resolved gear when thinking is on.

## Model

`/model` lists configured models, or selects one for the next idle
turn. The conversation stays in place; only the `<env>` model line,
status bar, context window, and chat client change. The command is
refused while a turn is running.

- `/model <model>` uses the current provider. A name that is unique in
  the catalog still works. A provider with one model can be selected
  by provider name alone.
- `/model <provider> <model>` switches providers. The model id is
  everything after the first space, so ids may contain `/`.
- Tab completes current-provider models, then a provider name, then
  that provider's models.
- Only models listed under `providers` can be selected. A missing API
  key leaves the current model unchanged.
- The new `provider` and `model` are written to `config.json`.

Switching models never fails because of thinking: unsupported thinking
is omitted, and an unsupported stored gear uses the provider default
without changing the stored choice.

## Slash commands

Type `/` to open the picker. Built-in verbs:

| Command | Aliases | Action |
| :--- | :--- | :--- |
| `/help` | `/h` | Shortcuts and commands |
| `/plan` | | Toggle Plan / Work |
| `/approval` | | Cycle or set `default`, `autoedit`, `review`, `fullreview`, `full` |
| `/thinking` | `/think` | Cycle or set the thinking gear |
| `/tokens` | | Toggle estimated progress tokens, or set `on` / `off` |
| `/model` | | List catalog models, or set `model` / `provider model` |
| `/status` | | Turns, tokens, mode, workspace |
| `/clear` | `/new` | Start a new conversation (new session id) |
| `/cd` | | Show the workspace, or set it to an existing directory (`~` is expanded) |
| `/resume` | `/continue`, `/sessions` | Replay the latest session for this workspace, or `/resume <id>` |
| `/compact` | `/summarize` | Summarize older context now (refused while a turn is running) |
| `/quit` | `/exit`, `/q` | Exit |

Unknown `/` text prints `unknown command`. `/cd` with no argument
prints the current workspace root. `/cd` only accepts a directory
that already exists. `/tokens` with no argument toggles the live
progress-row estimate (four characters per token, prefixed with `~`).
It is not provider-billed usage. The choice is written to `config.json`.

## Built-in tools

Filesystem and shell writes are fenced to the workspace root. Paths
that escape the root are rejected for `edit`, `write`, and `bash`.
`read`, `glob`, and `grep` may use an absolute path outside the
workspace after approval (you, or the Review model in Review or
FullReview). Credential paths
(`.ssh`, `.gnupg`, `credentials.json`) stay Forbidden. Glob and grep
skip `.git`, `.vs`, `bin`, `obj`, `node_modules`, and `dist`. Binary
files are rejected for read/edit (NUL probe). Shell working directory
is the workspace root.

| Tool | Catalog | Purpose |
| :--- | :--- | :--- |
| `read` | Plan, Work | Read a workspace text file (`path`, optional 1-based `offset` and `limit`) |
| `glob` | Plan, Work | List files matching a glob (`pattern`, optional `path`) |
| `grep` | Plan, Work | Regular-expression search (`pattern`, optional `path` and file-name `glob`) |
| `todowrite` | Plan, Work | Replace or merge the session todo list |
| `question` | Plan, Work | Ask one or more questions with single/multiple choices and custom answers |
| `skill` | Plan, Work | Load an available skill by `name` (omitted when `skills` is `false`) |
| `edit` | Work | Replace one unique `old_string` in a file |
| `write` | Work | Create or overwrite a UTF-8 text file |
| `bash` | Work | Run one shell command after approval (`bash -lc`, 120 second timeout) |

Practical limits: read up to 1,000,000 characters or 20,000 lines;
write up to 2 MiB; grep up to 500 matches and 8 MiB per file; glob
up to 1,000 matches; tool output truncated at 100,000 characters.

## External tools

Operators add extra catalog tools as **tool sets**: one directory, one
`tools.json`, one runner. Discovery is Crystal-owned only:

```text
~/.crystal/tools/<directory>/tools.json
<workspace>/.crystal/tools/<directory>/tools.json
```

A project directory of the same name replaces the home set as a whole.
`"enabled": false` in `tools.json` leaves the directory in place without
loading that set (default `true`). Set `"externalTools": false` in
`config.json` to skip discovery. When `true`, the field is omitted from
the written file, same as `skills`.

The set identity is the directory name (1–64 characters, start with a
letter, then letters, digits, `_`, `.`, `-`). There is no JSON `name`
or `id`. Each tool's model-facing name is `tools[].name` (exec) or
`ITool.Definition.Name` (dotnet). Built-in names win. Default
`catalogs` is Plan and Work; `"catalogs": ["work"]` is Work only.

Runners:

- **exec**: `ProcessStartInfo.ArgumentList`, no shell templates. Stdin
  is the fenced arguments object (default on). An `argv` map turns
  scalar properties into flags. Working directory is the workspace
  root.
- **dotnet**: a framework-dependent class library that implements
  `Crystal.Tools.ITool`. Every public non-abstract tool is loaded in
  one isolated load context for that set. Shared types are `Crystal`
  and `Crystal.Tools`; other dependencies stay private to the set.

Every external tool is at least Write + Workspace and still goes
through `ToolInvocationPolicy`. Full auto-passes a workspace-bounded
external write; AutoEdit does not. Review and FullReview send it to
the reviewing model. Details, manifest fields, and the
dotnet publish layout are in
[docs/external-tools.md](docs/external-tools.md).

## Prompts and instructions

Crystal is prompt-neutral. Every model-bound string this product
sends is authored here. Operators may replace Work, Plan, and Review
by placing files under `~/.crystal/prompts` and
`<workspace>/.crystal/prompts`.

Named files (`work.md`, `plan.md`, `review.md`; `.txt` is also
accepted):

- Overlay order: built-in default, then `~/.crystal/prompts`, then
  `<workspace>/.crystal/prompts`.
- A project file replaces the home file for that name.
- Empty files are treated as missing.
- The host never writes prompt files.

The built-in Work and Plan assistant name is Crystal Code. A host-owned
`<env>` block (workspace, git, platform, date, provider/model) is
appended after the Work or Plan body and is not overlayable. When
Skills is enabled, available-skill guidance is appended after `<env>`
and is also host-owned.

Workspace facts are appended under "Workspace instructions" on Work
and Plan only. Review is the named file alone so the reviewer stays
a safety check.

Instruction sources, in order:

1. `~/.crystal/instructions.md` (or `.txt`)
2. `<workspace>/.crystal/instructions.md` (or `.txt`)
3. `<workspace>/.crystal.md`
4. OpenCode-compatible rule files (below)

`AGENTS.md` and `CLAUDE.md` are extra instructions, not prompt
replacements. They never replace Work, Plan, or Review.

- Global: first existing file among `~/.crystal/AGENTS.md`,
  `~/.crystal/CLAUDE.md`, `~/.config/opencode/AGENTS.md`, and
  `~/.claude/CLAUDE.md` (`XDG_CONFIG_HOME` is honored for the
  OpenCode path).
- Project: walk from the workspace up to the git root. The first
  matching name wins (`AGENTS.md`, then `CLAUDE.md`, then
  `CONTEXT.md`). Every file of that name on the walk is appended.
  `CLAUDE.md` is used only when no `AGENTS.md` exists on the walk.

## Skills

Skills are OpenCode-compatible instruction folders. They are not
prompt overlays. The model sees a list of available skills and loads
one with the `skill` tool. When Skills is enabled, `read`/`glob`/`grep`
of any path inside a Skills search directory (`skill` / `skills`
trees, including scripts and other files that are not `SKILL.md`)
auto-passes; it does not ask you. Set `"skills": false` in
`config.json` to disable the tool, the guidance, and that auto-pass.
Other files outside the workspace still need approval (you, or the
Review model in Review or FullReview).

Each skill is a directory with a `SKILL.md` that starts with YAML
frontmatter (`name` and `description` required). The skill id is the
directory name when it matches `^[a-z0-9]+(-[a-z0-9]+)*$` (1–64
characters). Frontmatter `name` may be that id or a display title
and does not have to match the directory. If the directory name is
not a valid id, a valid frontmatter `name` is used instead. Folded
(`>`) and literal (`|`) YAML descriptions are accepted.

Discovery follows OpenCode's global and project walk. Later sources
overwrite earlier ones with the same name. Crystal-native paths win
over OpenCode-compatible paths.

Global:

1. `~/.claude/skills/<name>/SKILL.md`
2. `~/.agents/skills/<name>/SKILL.md`
3. `~/.config/opencode/{skill,skills}/<name>/SKILL.md` (`XDG_CONFIG_HOME`
   is honored)
4. `~/.opencode/{skill,skills}/<name>/SKILL.md`
5. `~/.crystal/{skill,skills}/<name>/SKILL.md`

Project, walking from the workspace up to the git root:

1. `.claude/skills/<name>/SKILL.md`
2. `.agents/skills/<name>/SKILL.md`
3. `.opencode/{skill,skills}/<name>/SKILL.md`
4. `.crystal/{skill,skills}/<name>/SKILL.md`

`/cd` reloads skills and external tool sets from the new workspace. `/cd`
and resume also reload prompts from the current workspace. Resume
refreshes the first system message from the current prompt files, the
current `<env>` block, and current skill guidance.

## Sessions

Sessions are written to `~/.crystal/sessions/<id>.json` after each
completed turn, and again on an orderly exit when the transcript has
a user message. The file stores the transcript, todos, last
provider-reported token usage, and turn counts.

`/quit` and two Ctrl+C presses on an empty composer leave the
alternate screen, then print the session id and `crystal --resume <id>`.
`--resume` loads that file before the alternate screen. A missing or
empty session exits without entering the TTY. `/resume` still replays
from inside a running session.

| Command | Effect |
| :--- | :--- |
| `crystal --resume <id>` | Load that file under `~/.crystal/sessions` at process start |
| `/resume` | Load the latest session for this workspace and replay the transcript |
| `/resume <id>` | Load that file under `~/.crystal/sessions` |
| `/clear` | Start a new id |

Resume also restores the last usage snapshot so the status bar and
compaction still have a baseline before the next model call. A compacted
session restores the summary and recent tail; only the live system
prompt is refreshed.

## Compaction

Crystal does not reduce context. When estimated or reported tokens
cross `compactionThreshold` of the selected model's usable window, the
host:

1. Clears old tool results outside a protected recent band, when that
   frees enough tokens.
2. Asks the model for a structured summary of older turns (folding any
   previous summary) and keeps a recent tail verbatim.
3. Stops if the summary cannot be produced and nothing else can be
   reduced. Compaction does not loop.

`/compact` (alias `/summarize`) runs this immediately. It is refused
while a turn is running. A successful compact is written to the session
file; `/resume` restores the summary and tail, and refreshes only the
live system prompt. The transcript prints `compacting context...`
while this runs.

## Data directory

Override with `CRYSTAL_HOME` or `--home`.

```text
~/.crystal/
  binaries/code/CrystalCode (CrystalCode.exe on Windows)
  config.json
  credentials.json
  permissions.json
  instructions.md
  AGENTS.md
  prompts/
    work.md
    plan.md
    review.md
  skill/<name>/SKILL.md
  skills/<name>/SKILL.md
  tools/<directory>/tools.json
  sessions/<id>.json
  logs/
  plugins/
```

Project overlay (named prompts, Crystal skills, and tool sets of the
same directory name win over home):

```text
<workspace>/.crystal/
  instructions.md
  prompts/
    work.md
    plan.md
    review.md
  skill/<name>/SKILL.md
  skills/<name>/SKILL.md
  tools/<directory>/tools.json
<workspace>/.crystal.md
<workspace>/AGENTS.md
```

`plugins/` is reserved. The current product does not load `IPlugin`
assemblies from that directory. Dotnet tool sets load class libraries
from the set directory only.

## Environment variables

| Variable | Meaning |
| :--- | :--- |
| `CRYSTAL_HOME` | Data directory instead of `~/.crystal` |
| `DEEPSEEK_API_KEY` | DeepSeek key (overrides `credentials.json`) |
| `OPENAI_API_KEY` | OpenAI key (overrides `credentials.json`) |
| `<PROVIDER>_API_KEY` | Key for a named provider (hyphens become underscores) |
| `CRYSTAL_API_KEY` | Shared fallback key |
| `XDG_CONFIG_HOME` | Base for the global OpenCode `AGENTS.md` fallback |

Do not pass secrets on the command line. They appear in process
lists.

## Safety

- Workspace tools reject paths that leave the workspace root.
- Credential and keyring paths are classified Forbidden.
- Forbidden and Privileged actions never fully auto-pass.
- Approval goes through `Crystal.Tools.ToolInvocationPolicy`. Side
  effects are not invoked by bypassing the executor.
- Runtime text is plain English. No emoji in exceptions, logs, or UI
  chrome.
- Secrets must not appear in source, logs, diagnostics, or commits.

## Documents

- [BUSINESS.md](BUSINESS.md) — product boundary
- [ARCHITECTURE.md](ARCHITECTURE.md) — ownership and runtime
- [STANDARDS.md](STANDARDS.md) — engineering rules
- [AGENTS.md](AGENTS.md) — instructions for coding agents
- [docs/external-tools.md](docs/external-tools.md) — operator tool sets
