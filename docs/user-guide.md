# Crystal Code

Detailed usage reference. For a shorter introduction, start with the
[project README](../README.md).

A production coding TUI for local repositories. The terminal is the
only operator surface. It runs a streaming model-and-tool loop, with
Plan/Work modes, risk-aware approval, automatic context compaction,
and operator data under `~/.crystal`. `crystal run` runs one task
without a terminal; anything that would ask the operator is denied.

CrystalCode consumes the [Crystal](https://github.com/YELANDAOKONG/Crystal) library. It does not modify Crystal.
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
- compacts conversation context when usage approaches the selected
  model's window;
- persists configuration, permissions, and sessions under `~/.crystal`;
- talks to DeepSeek and OpenAI-compatible Chat Completions, OpenAI Responses,
  and Anthropic Messages endpoints, including operator-added gateways.

Built-in tools and all provider adapters register through the same in-process
plugin table. Operators add extra catalog tools as
tool sets under `~/.crystal/tools` and `<workspace>/.crystal/tools`.
Operators add plugins under `~/.crystal/plugins` and
`<workspace>/.crystal/plugins`.

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

## Not yet implemented

The following planned capabilities are not yet implemented in the current
build: MCP servers, an operating-system sandbox,
parent/child Agents, audio and video input, non-text model output,
editing provider definitions in the TUI, automatically fetching
model-list information from a provider, and switchable thinking-chain
compression. That last one would, after each thinking pass, ask a model
to compress and summarize that thinking according to a requirement.
Context compaction, microcompaction, `/verbose thinking`, and a
provider's summarized thinking display are not that path.
Built-in provider protocols are DeepSeek, OpenAI-compatible Chat Completions,
OpenAI Responses, Anthropic Messages, Gemini, and Ollama. A plugin may add
another protocol. Image input is available for supported models and providers.

## Requirements

Linux and macOS are the recommended platforms. On Windows, install [Git for Windows](https://git-scm.com/install/windows) so Git Bash provides `bash` on the `PATH`.

- An API key for the selected provider in `~/.crystal/credentials.json`,
  or in the process environment for one launch (see [Credentials](#credentials))
- A TTY for the interactive alternate-screen UI. `crystal run` does not
  need a TTY
- `bash` on the `PATH` (Git Bash is used on Windows when present)

Building from source also requires:

- .NET 10 or .NET 11 SDK
- A sibling checkout of [Crystal](https://github.com/YELANDAOKONG/Crystal) at `../Crystal` (relative to this
  repository root)

## Build

From the repository root:

```bash
dotnet build CrystalCode.sln
dotnet test CrystalCode.sln
```

To install this checkout for local development:

```bash
sh scripts/install-local.sh
```

On Windows:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install-local.ps1
```

The script checks for the .NET 10 or .NET 11 SDK, the sibling Crystal
checkout, and the current platform. It then publishes Release into `build/`
with the same arguments as the release workflow:

```text
dotnet publish CrystalCode/CrystalCode.csproj
  --configuration Release
  --runtime <rid>
  --self-contained true
  -p:PublishSingleFile=true
  --output build
```

The workflow writes `./publish` for the GitHub asset. This script writes
`build/`, which is gitignored. It replaces `~/.crystal/binaries/code/`
and leaves configuration, credentials, and prompts in place. Each check
is printed before the script continues. The script locates the repository
from its own path.

The executable project is `CrystalCode`. The Spectre application
name is `crystal`. The session engine lives in the `CrystalCode.Engine`
class library, which the executable references.

## Run

Start from the workspace the agent should edit. The current directory
is the workspace unless `--workspace` is set.

```bash
dotnet run --project CrystalCode -- --provider deepseek --model deepseek-flash
```

CLI options:

| Option | Meaning |
| :--- | :--- |
| `-p`, `--provider <name>` | Provider name (`deepseek`, `openai`, `gemini`, `ollama`, or a name you added to `providers.json`) |
| `-m`, `--model <id>` | Model id listed under that provider |
| `-w`, `--workspace <path>` | Workspace root (default: current directory) |
| `--home <path>` | Data directory (default: `CRYSTAL_HOME`, then `~/.crystal`) |
| `-r`, `--resume [id]` | Choose a saved session. Omit the value to list this workspace. An id loads that file and stays here. A directory lists that workspace and enters it. `all` lists every workspace and enters the chosen session's directory |
| `--approval <mode>` | `default`, `edit`, `review`, `audit`, or `full` for this process |
| `--approval-model <on\|off>` | Use the saved approval model, or turn it off for this process |
| `--approval-provider <provider>` | Approval-model provider for this process. Turns the switch on |
| `--approval-model-id <model>` | Approval-model id for this process. Turns the switch on |
| `--approval-thinking <effort>` | Reviewer thinking gear for this process. Does not change `approvalModel.thinkingEffort` on disk |
| `--compaction-model <on\|off>` | Use the saved compaction model, or turn it off for this process |
| `--compaction-provider <provider>` | Compaction-model provider for this process. Turns the switch on |
| `--compaction-model-id <model>` | Compaction-model id for this process. Turns the switch on |
| `--compaction-thinking <effort>` | Summarizer thinking gear for this process. Does not change `compactionModel.thinkingEffort` on disk |
| `--plan` / `--work` | Start in Plan or Work. Omit both to keep Work, or the mode saved on a resumed session |
| `--thinking <effort>` | Same values as `/thinking` |
| `--context <budget>` | Local context budget for the selected model when it declares more than one. Same text as `/context` (`272k`, `1m`, or an exact token count). A model with one budget, or a budget it does not declare, is rejected. Does not write `contextSelections` |
| `--tier <token>` | Service-tier token the selected model lists. Does not write `tierSelections` |
| `--speed <token>`, `--fast <token>` | Speed token the selected model lists. Does not write `speedSelections` |
| `--prompt-set <name>` | Force one home prompt set for this process, even when its `prompt.json` says `enabled` false. `default` forces the built-in text. `/promptset` switching is refused and does not write the file. Listing and export stay available |
| `--prompt-attachments <on\|off>` | `on` follows each attachment's `enabled` flag and `order`. `off` appends nothing and refuses enable, disable, up, and down. Listing stays available |
| `--prompt-attachment <name>` | Repeatable. Appends that attachment after the enabled set for this process, even when its file says `enabled` false. Does not write the file. Rejected together with `--prompt-attachments off` |
| `--skills`, `--external-tools`, `--plugins` | `on` or `off` for this process |
| `--rules <value>` | `on` or `off` for both rule-file sides, or `global=on\|off` and `project=on\|off`. Does not affect `instructions.md` or `.crystal.md` |
| `--model-calls <count>` | Model rounds for each turn. `unlimited` removes that cap |
| `--tool-calls <count>` | Tool calls for each turn. `0` allows none. `unlimited` removes that cap |
| `--duration <seconds>` | Wall-clock cap for each turn. `unlimited` removes that cap |
| `--bash-timeout <seconds>` | Foreground per-command bash cap. `unlimited` disables that timer |
| `--disable-tool <name>` | Repeatable. Omits that built-in tool for this process. An unknown name refuses to start. Does not write the file, and does not free the name |

These flags shape this process only. They are not written to `config.json`
or `prompt.json` at startup. A later slash command that saves a preference
writes that preference. It also writes `--provider` and `--model` when those
were passed. It does not write the other flags above. `--plan` and `--work`
together are rejected. `crystal space` accepts the same flags except
`--workspace`.

`--help` prints the same options.

The first run creates `~/.crystal` (or `--home` / `CRYSTAL_HOME`) and
writes a starter `config.json` if one is missing. Defaults are
provider `deepseek`, model `deepseek-flash`, approval `default`,
and compaction at 80% of the selected model's `contextWindow`.

The first interactive launch in a directory asks whether you trust it
when `workspaceTrust` is on (the default). Inside a git repository the
question names the repository root, and trusting it covers later
launches in that repository. Outside a repository only that directory
is trusted. Yes is written to `~/.crystal/trusted.json`. No exits
without writing that file, so the next launch asks again. A redirected
terminal cannot ask and exits with an error. `/trust off` skips later
prompts. `/cd` into an untrusted root asks again; No stays in the
current workspace.

If the provider has more than one model and neither `config.json` nor
`--model` picks one, the process exits and asks for `--model`.

## Operator space

`crystal space` creates `~/.crystal/space` when it is missing and opens the
terminal there. `--home` selects the data directory, so the space is
`{home}/space`. The command accepts the same session flags as `crystal`,
including `--provider`, `--model`, `--resume`, `--prompt-set`, and the other
process-only overrides. It does not accept `--workspace`.

That directory is trusted without a prompt and without a `trusted.json`
entry. `/space` creates it when missing and switches there. It uses the
same trust and reload path as `/cd`, and does not ask. Trust does not climb to a parent git
repository, and `/trust forget` does not remove it. A subdirectory follows
the ordinary trust rule. If `space` itself is a git repository, its children
share that trust root. Sessions there are saved under the absolute path,
the same way as any other workspace.

`crystal run --space` creates that directory when it is missing and runs
one task there. It does not write a trust entry. `--space` cannot be
combined with `--workspace`.

## Version

`crystal version` and `crystal --version` print the build identity and exit.
They do not open the terminal and do not read configuration.

`crystal plugins` and `crystal tools` list, show, enable, or disable one
directory. They print a Spectre.Console table unless `--format text` is
set. They do not open a session and do not write `config.json`.

`crystal promptsets` and `crystal prompt-attachments` do the same for
`prompt.json`. Prompt sets are Home-only: `list`, `show`, `enable`, and
`disable` accept `--home` and `--format text`. Enabling one set turns the
others off. Prompt attachments also accept `--workspace` and
`--source home|project`. Neither command creates a missing directory or
writes `config.json`.

```text
Crystal Code   3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c7d6e5f4a
Crystal        1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d
SDK            10.0.201
Configuration  Debug

Runtime        .NET 10.0.4
OS             Ubuntu 24.04.2 LTS x64
```

`Crystal Code` is the commit of this repository. `Crystal` is the commit of
the Crystal library repository. `SDK` is the .NET SDK that compiled the
executable. `Configuration` is the MSBuild configuration recorded on the
executable, usually `Debug` or `Release`. A blank line separates that build
from the process host. `Runtime` is the shared framework hosting this
process. `OS` is the runtime's operating-system description plus the
architecture. A line is omitted when that value was not recorded, and an
empty section is omitted with the blank line. The
commits, SDK version, configuration, runtime, and operating system above
are examples.

## Headless run

`crystal run` runs one task and exits. It does not open the alternate
screen. Quote the task, or omit it and pipe the task on stdin. A task
argument wins over stdin. Slash commands are rejected.

```bash
crystal run --workspace . --approval review --duration 600 "Fix the failing test."
echo "Summarize the repository." | crystal run --model-calls 8 --tool-calls 32
```

`--format default` prints a readable trace: the assistant reply, then each
tool beside its own result. `read`, `list`, `glob`, and `grep` keep a short head
excerpt and say how many lines were omitted. `edit` and `write` keep their
result. `bash` keeps the command, the exit status, and a short tail, with
a longer tail when the command fails. Other successful tools use the same
short excerpt. Failures other than `bash` keep the result text.
Labels use square brackets, exit status uses parentheses, and omitted-line
markers use angle brackets, so host text stays distinct from tool output.
`--format json` prints one JSON object per line and keeps the full tool
output. Thinking text is omitted unless `--show-thinking` is set. With that
flag, thinking is printed under `[Thinking]` and the reply under `[Assistant]`.
The saved session id is printed at the end. Resume that session with the
interactive `crystal --resume <id>`.

Flags override the saved configuration for this process only. They are
not written to `config.json`. Do not put secrets on the command line.
Omitted flags keep the saved value, or the product default when the
saved value is also unset. `--workspace-trust` does not: omitting it
checks the directory. `unlimited` removes that one cap.

| Option | Meaning |
| :--- | :--- |
| `-p`, `--provider` / `-m`, `--model` | Provider and model for this process |
| `-w`, `--workspace` / `--home` | Workspace root and data directory |
| `--space` | Use the operator space `{home}/space` as the workspace. Creates it when missing. Cannot be combined with `--workspace` |
| `--approval <mode>` | `default`, `edit`, `review`, `audit`, or `full` |
| `--approval-model <on\|off>` | Use the saved approval model, or turn it off for this process |
| `--approval-provider <provider>` | Approval-model provider for this process. Turns the switch on |
| `--approval-model-id <model>` | Approval-model id for this process. Turns the switch on |
| `--approval-thinking <effort>` | Reviewer thinking gear for this process. Same values as `/approval thinking`; an unsupported gear falls back to the provider default and a note is printed, and a value that is not an effort token exits 1. Does not write the file |
| `--compaction-model <on\|off>` | Use the saved compaction model, or turn it off for this process. `on` needs a saved selection, or `--compaction-provider` and `--compaction-model-id` on the same command. Does not write the file |
| `--compaction-provider <provider>` | Compaction-model provider for this process. Turns the switch on |
| `--compaction-model-id <model>` | Compaction-model id for this process. Turns the switch on |
| `--compaction-thinking <effort>` | Summarizer thinking gear for this process. Same values as `/compact thinking`. An unsupported gear falls back to the provider default and a note is printed. Does not write the file |
| `--plan` / `--work` | Start in Plan or Work. The default is Work |
| `--thinking <effort>` | Same values as `/thinking`. An unsupported gear is ignored, a note is printed, and the request uses the provider default. A value that is not an effort token exits 1 |
| `--context <budget>` | Local context budget for the selected model when it declares more than one. Same text as `/context`. A model with one budget, or a budget it does not declare, exits 1. Does not write `contextSelections` |
| `--tier <token>` | Service-tier token the selected model lists. A model that does not list it exits 1. Does not write `tierSelections` |
| `--speed <token>`, `--fast <token>` | Speed token the selected model lists. A model that does not list it exits 1. Does not write `speedSelections` |
| `--prompt-set <name>` | Force one home prompt set for this process, even when its `prompt.json` says `enabled` false. `default` forces the built-in text. Does not write the file |
| `--prompt-attachments <on\|off>` | `on` follows each attachment's `enabled` flag and `order`. `off` appends nothing. Omitted means `on`. Does not write the file |
| `--prompt-attachment <name>` | Repeatable. Appends that attachment after the enabled set, even when `enabled` is false. A missing name exits 1. Cannot be combined with `--prompt-attachments off`. Does not write the file |
| `--skills`, `--external-tools`, `--plugins` | `on` or `off` for this process |
| `--rules <value>` | `on` or `off` for both rule-file sides, or comma-separated `global=on\|off` and `project=on\|off`. An assignment leaves the other side as saved. Does not write the file, and does not affect `instructions.md` or `.crystal.md` |
| `--workspace-trust <on\|off>` | Directory trust for this process only. Omitted and `on` refuse an untrusted directory. `off` skips the check and does not record trust. This does not follow `workspaceTrust` |
| `--model-calls <count>` | Model rounds for this turn. Default 1024 |
| `--tool-calls <count>` | Tool calls for this turn. Default 8192. `0` allows none |
| `--duration <seconds>` | Wall-clock cap for this turn. Default 7 days. Pass a short value in CI |
| `--bash-timeout <seconds>` | Foreground per-command bash cap. Default 600. `unlimited` disables that timer |
| `--disable-tool <name>` | Repeatable. Omits that built-in tool for this process. An unknown name refuses to start. Does not write the file, and does not free the name |
| `--show-thinking` | Print reasoning text for this run, and label each reply `[Assistant]`. Independent of `verboseThinking` |
| `--format` | `default` for plain text, or `json` for one JSON object per line |

`--approval-model off` cannot be combined with `--approval-provider` or
`--approval-model-id`. `--approval-model on` needs a saved approval model
or `--approval-model-id`. `--approval-thinking` sets a gear for this
process only: `/approval model` keeps the saved gear, and
`/approval thinking` is the command that writes one. The process gear is
ignored while the switch is off.

`--workspace-trust` is not one of the flags that inherit the saved
setting. Leaving it out checks the directory even when `workspaceTrust`
is false in `config.json`. An untrusted directory exits 4 before a
session starts. Trust it from the interactive terminal, or pass
`--workspace-trust off` for that process. `crystal run --space` uses the
operator space, which is already trusted.

There is no operator. In Review and Audit the reviewing model still
judges calls that require review: allow runs the tool, and deny returns
the reviewer's reason to the model. That model is the session model
unless the approval-model switch is on for this process. Anything that
would ask the operator, including Default, Edit, and Full prompts, is
denied with the usual rejection text. The `question` tool is dismissed.
Plan does not offer write, edit, or bash. Credential paths stay forbidden.

A reviewer denial is a tool error. If the model then finishes, the
process exits 0. A denied operator prompt or a dismissed question does
not. A call to a tool Plan does not offer fails the turn.

| Exit | Meaning |
| :--- | :--- |
| 0 | The turn completed, and no operator prompt was denied or dismissed |
| 1 | The command, configuration, credentials, workspace, or prompt set is invalid |
| 2 | The model request failed, or the model stopped because of a content filter |
| 3 | A model-call, tool-call, or duration budget was reached, the context overflowed, or the model output was truncated |
| 4 | The turn finished after an operator prompt was denied or a question was dismissed |
| 5 | The run was interrupted |

A failure or a budget stop is reported instead of a denial. An interrupt
is reported instead of a denial. Plain stdout then includes `[Stopped]` and
the session id.

Each JSON line has `type`, a UTC `timestamp`, and `sessionID`. The types
are `step_start`, `text`, `reasoning`, `tool_use`, `error`, `note`,
`retry`, `step_finish`, `stopped`, and `session`. `tool_use` carries
`name`, `callId`, `arguments`, `status` (`success` or `failure`), and
`output`. `step_finish` carries `reason`, `modelCalls`, and `toolCalls`.
`reasoning` is present only with `--show-thinking`. `stopped` is present
only when the process will exit non-zero. `session.text` is the same
resume hint the plain format prints. Invalid commands and missing
credentials are still plain text on stderr.

## Credentials

Do not put secrets in the workspace, in this repository, or in commit
contents. CrystalCode never writes secrets into the project tree.

Store a persistent API key in `~/.crystal/credentials.json`, keyed by
provider name. The value is plain text. Where the operating system
allows it, the file is created with owner-only permissions. Child
processes do not inherit this file. Use it for an interactive install.

A process environment variable overrides the file. Set one for a single
launch or for a CI runner that injects the key into that process. A key
written into a shell startup file is loaded by login shells, including
the login shell the product starts for model-invoked commands, and those
files are often readable by users other than the owner.

Resolution order for the active provider:

1. Process environment (see below)
2. `apiKey` on the effective provider definition
3. `~/.crystal/credentials.json`, keyed by provider name

The effective definition is the provider entry in `providers.json`. When
that file is absent, it is the entry under the legacy `config.json.providers`
field. When `providers.json` exists, the legacy field is not used for the
catalog or for this key lookup. See [Configuration](#configuration).

Environment names, in order:

1. `apiKeyEnvironment` on that same definition, when set
2. `<PROVIDER>_API_KEY` derived from the provider name (hyphens
   become underscores; for example `DEEPSEEK_API_KEY`,
   `OPENAI_API_KEY`, `OPENROUTER_API_KEY`)
3. `CRYSTAL_API_KEY` (shared fallback)

A provider-specific variable wins over `CRYSTAL_API_KEY`.

`apiKey` on that definition may be one of:

| Form | Meaning |
| :--- | :--- |
| `{env:NAME}` | Read the named process environment variable |
| `{file:path}` | Read a file (relative to `~/.crystal`, or absolute; `~` is expanded) |
| a literal string | Used as-is (avoid this in shared files) |

Keep the secret out of `providers.json`. A key that stays on this machine
belongs in `credentials.json`. `{env:NAME}` fits a launch or CI job that
already exports the variable. `{file:path}` fits a key that already lives
in another owner-only file.

`credentials.json` shape:

```json
{
  "deepseek": {
    "apiKey": ""
  }
}
```

Leave the value empty in examples and in any file that might be
shared. Put the real secret in the local `credentials.json`.

If no key is found, the process prints an English error and exits
with status 1. It does not print the secret.

## Configuration

Host settings live in `~/.crystal/config.json`. Provider and model definitions
live in `~/.crystal/providers.json`; the built-in DeepSeek and OpenAI catalog
is available even when this file is absent. Operator definitions overlay that
catalog. Edit the file you changed, then restart for the changes to take effect.
Editing provider definitions in the TUI is deferred. A
`<workspace>/.crystal/config.json` is not read; workspace-level configuration
is deferred.

When `providers.json` exists, it supplies the operator catalog, and the legacy
`config.json.providers` field is not read. When `providers.json` is absent,
that legacy field is read instead, including its `apiKey` and
`apiKeyEnvironment` values. Saving preferences does not rewrite an existing
`providers.json`. If that file is absent and `config.json` still has a
`providers` object, the save copies that object to `providers.json` and keeps
the original field in `config.json`. After the copy, later edits belong in
`providers.json`.

Top-level fields:

| Field | Meaning |
| :--- | :--- |
| `provider` | Active provider name |
| `model` | Active model id (must exist under that provider) |
| `approval` | `default`, `edit`, `review`, `audit`, or `full` |
| `approvalModel` | Optional reviewer. `enabled`, `provider`, `model`, and optional `thinkingEffort`. Omitted means off. `enabled: false` keeps a stored provider, model, and gear unused |
| `compactionModel` | Optional summarizer. `enabled`, `provider`, `model`, and optional `thinkingEffort` (`default`, `off`, or a gear that model lists). Omitted means off, and summaries use the session model. `enabled: false` keeps a stored provider, model, and gear unused. The model does not need `imageInput`. Opening it requires a catalog model; a missing credential or unknown model is refused and the saved value stays |
| `imageModel` | Optional vision model for `describe_image`. `provider`, `model`, and optional `thinkingEffort`. Omitted means the tool is absent. The model must have `imageInput`. Thinking capability stays on the model entry |
| `thinkingEffort` | Host thinking gear: `default`, `off` (`none` is the same), or an effort token the model lists. `max` and `maximum` are different tokens. Tokens are stored in lowercase and sent unchanged |
| `contextSelections` | Saved local context budgets as a nested map, for example `{"openai": {"my-model": 272000}}`. `/context` writes this map; the older flat `{"openai/my-model": 272000}` form is still read and is rewritten nested on the next save. An absent or unsupported choice uses that model's lowest declared budget |
| `tierSelections` | Saved service-tier tokens as a nested map, for example `{"openai": {"my-model": "flex"}}`. `/tier` writes this map. An absent choice, or a token the model no longer lists, omits the field |
| `speedSelections` | Saved speed tokens in the same shape as `tierSelections`. `/speed` writes this map |
| `skills` | Enable the `skill` tool and available-skill guidance (default `true`) |
| `ruleFiles` | OpenCode-compatible rule files. `global` and `project` each default to `true` and are omitted until off. Does not affect `instructions.md` or `.crystal.md` |
| `externalTools` | Enable operator tool set discovery (default `true`) |
| `plugins` | Enable operator plugin discovery (default `true`) |
| `workspaceTrust` | Ask before the first interactive session in a directory (default `true`; omitted when on). Does not apply to `crystal run` |
| `externalToolApproval` | Per-source trust for tool-set author declarations: `home` and `project`, each `author` or `host` (defaults Home `author`, Project `host`) |
| `estimatedTokens` | Show a live four-characters-per-token estimate on the progress row during Thinking and Writing (default `false`) |
| `verboseResults` | Parent switch for success result panels (default `true`; omitted when on). Off hides every success result already on screen, including read, search, skill, edit, write, bash, and background bash. Does not hide Tool cards; those follow `verboseCalls`. Failures stay. `verboseTools` and `verboseCommands` do not draw results while this is off. `crystal run` does not read it |
| `verboseCalls` | Show Tool cards (default `true`; omitted when on). Off hides every Tool card already on screen: read, search, edit, write, bash, background bash, and external tools such as Apply Patch. Success and failure results stay. Independent of `verboseResults`, `verboseTools`, and `verboseCommands`. `crystal run` does not read it |
| `verboseTools` | While `verboseResults` is on, show read, search, and skill result panels (default `true`; omitted when on). Edit and write results stay. No visible effect while `verboseResults` is off |
| `verboseCommands` | While `verboseResults` is on, show full output from `bash`, `bash_start`, `bash_status`, `bash_logs`, and `bash_stop` (default `true`; omitted when on). Off keeps a hidden-line hint and the last line. No visible effect while `verboseResults` is off |
| `verboseApprovals` | Show policy and grant auto-pass cards (default `true`; omitted when on). Off hides those cards already on screen. Review allow cards follow `verboseReview`. The ask overlay stays |
| `verboseReview` | Show review allow cards (default `true`; omitted when on). Off hides those cards already on screen, including Outcome and rationale. Independent of `verboseApprovals`. The ask overlay, reviewer Deny text, and `crystal run` stay |
| `verboseThinking` | Show Thinking panels in the interactive transcript (default `true`; omitted when on). Off hides live and restored cards. The gear, progress row, and export stay. `crystal run --show-thinking` is separate |
| `exportDirectory` | Export root: omitted or `home` for `{home}/exports`, `workspace` for `<workspace>/.crystal/exports`, or any absolute/`~` path |
| `customStatusLine` | Enable the ordered custom status line (default `false`; the existing adaptive status line remains the default) |
| `statusLine` | Ordered custom fields used only when `customStatusLine` is enabled. Built-in names are checked on load. An extra name is kept when it matches a plugin field name, even if that plugin is not loaded yet |
| `compactionThreshold` | Fraction of the selected context budget that triggers compaction (greater than 0, at most 1; default `0.8`) |
| `showCompactionSummary` | Print the summary after a manual `/compact` (default `true`; omitted when on). `/compact summary` toggles it; `on` or `off` sets it |
| `showAutomaticCompactionSummary` | Print the same summary after automatic compaction (default `false`; omitted when off). `/compact auto` toggles it (`automatic` is the same); `on` or `off` sets it. Does not change `/compact` |
| `microCompaction` | Optional removal of old successful tool results before each model call. Omitted means off. Object with `enabled` (default `false`), `protectTurns` (at least 1, default `2`), `protectTokens` (non-negative, default `40000`), and `minimumTokens` (non-negative, default `20000`). Invalid values stop startup. `/compact micro` toggles it; `on` or `off` sets it. Does not summarize |
| `bashTimeoutSeconds` | Built-in `bash` per-command timeout. Omitted keeps 600 seconds. A positive integer up to 4,294,967 replaces it. `null` or `"unlimited"` disables that timer; cancelling the turn still stops the command |
| `backgroundBashTimeoutSeconds` | `bash_start` background task timeout. Omitted keeps 10800 seconds (3 hours). A positive integer up to 4,294,967 replaces it. `null` or `"unlimited"` disables that timer; `bash_stop` and session shutdown still stop the task |
| `retry` | Work-turn and compaction retry policy. Object with `enabled` (default `true`; `false` disables retries), `maximumRetries` (0-100, default 5), `initialDelaySeconds` (0-3600, default 2), `backoffFactor` (1-100, default 2), `jitterFactor` (0-1, default 0.25), and `maximumDelaySeconds` (0-86400, default 30; the backoff cap when Retry-After is absent). Invalid values stop startup. Work retries publish `RetryScheduled` |
| `approvalRetry` | The same object shape for the Review and Audit approval reviewer. Independent of `retry`. Reviewer retries are silent; exhausted retries fall back to your approval prompt |

Add named endpoints and model tables in `providers.json`, not in new
`config.json` configurations.

There is no global context window. Models that are not listed cannot
be selected.

### Built-in providers

Starter catalog (merged with `providers.json`):

| Provider | Protocol | Default base URI | Starter models |
| :--- | :--- | :--- | :--- |
| `deepseek` | `deepseek` | `https://api.deepseek.com/` | `deepseek-flash`, `deepseek-v4-flash` (compatibility alias), `deepseek-v4-pro` |
| `openai` | `openai` | `https://api.openai.com/v1/` | `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna` |
| `gemini` | `gemini` | `https://generativelanguage.googleapis.com/v1beta/` | `gemini-3.8-flash`, `gemini-3.1-pro-preview` |
| `ollama` | `ollama` | `http://localhost:11434/` | `qwen3:8b` (pull this model before selecting it) |

Built-in DeepSeek V4 models enable thinking with efforts `low`,
`high`, and `max`. Each has a 1,000,000-token context window.
Starter OpenAI models use a 400,000-token window and do not enable
thinking unless you add it.
The Ollama starter assumes a 4,096-token local context. If the local model
uses a different context size, set that size in `providers.json` and configure
the Ollama model to match it. Other local or cloud model IDs may be added there.
Automatically fetching model-list information from a provider is planned
and is not available in this build.

Anthropic Messages is also a built-in protocol, but the starter catalog
ships no entry for it, so Claude models come from `providers.json`. See
[Example: add an Anthropic (Claude) provider](#example-add-an-anthropic-claude-provider).

### Provider fields

| Field | Meaning |
| :--- | :--- |
| `protocol` | `deepseek`, `openai`, `responses`, `anthropic`, `gemini`, or `ollama` |
| `baseUri` | Absolute API base URI; the adapter appends the protocol's chat path |
| `organization` | Optional OpenAI organization for the `openai` protocol |
| `project` | Optional OpenAI project for the `openai` protocol |
| `replayReasoningContent` | Replay provider reasoning content (DeepSeek always replays it); reasoning that cannot be replayed keeps its readable text (assistant text, or the protocol's readable reasoning field) |
| `tokenLimit` | Chat Completions output field: `max_tokens` or `max_completion_tokens` (ignored by `responses` and `anthropic`) |
| `apiKeyEnvironment` | Environment variable name checked first for this provider |
| `apiKey` | Literal, `{env:NAME}`, or `{file:path}` |
| `requiresApiKey` | Whether a missing key is an error (default `true`, except `ollama`; set `false` for an unauthenticated endpoint) |
| `options` | Optional object of protocol-specific settings. Values are JSON strings, numbers, or booleans. Unknown names, and names that do not apply to `protocol`, fail loading. `temperature`, `topP`, and `maxTokens` stay on the model |
| `models` | Table of selectable model ids |

Provider names are letters, digits, hyphen, or underscore.

All protocol adapters send the constant `User-Agent: Crystal Code`, with no
version. `responses` authenticates with `Authorization: Bearer`; `anthropic`
uses `x-api-key` and `anthropic-version: 2023-06-01`. Both adapters use direct
HTTP and JSON/SSE handling; no provider SDK is required.
Gemini uses `x-goog-api-key` and native `generateContent` SSE. Ollama uses
native `/api/chat` JSON lines and omits authorization when no key is configured.
For a provider with `requiresApiKey: false`, an explicit provider key or its
specific environment variable is still used; `CRYSTAL_API_KEY` is skipped.

Amazon Bedrock models that support its OpenAI-compatible Chat Completions API
can be configured with `protocol: "openai"`, a regional
`https://bedrock-runtime.REGION.amazonaws.com/openai/v1/` base URI, and a
Bedrock API key. This path uses bearer authentication. IAM SigV4 credentials
are not supported by the current adapter.

### Model fields

| Field | Meaning |
| :--- | :--- |
| `contextWindow` | Required positive maximum model context size in tokens. Upper bound for selectable local budgets; used as the sole budget when `contextWindows` is omitted or empty |
| `contextWindows` | Optional distinct positive token budgets no greater than `contextWindow`, for example `[272000, 1000000]`. `/context` cycles these values; the lowest is the initial budget. Compaction and the status bar use the selected budget |
| `temperature` | Optional, 0 to 2 |
| `topP` | Optional, 0 to 1 |
| `maxTokens` | Optional positive output-token cap |
| `thinking` | Whether the model accepts reasoning hints |
| `thinkingEfforts` | Effort tokens this endpoint accepts. A token starts with a letter and then uses only letters, digits, underscores, and hyphens. `max` and `maximum` are different tokens. The host stores each token in lowercase and sends it unchanged |
| `thinkingCanDisable` | Whether `/thinking off` is accepted (default `true`). Built-in Gemini 3 models set this to `false` |
| `serviceTiers` | Service-tier tokens this model accepts. A token starts with a letter and then uses only letters, digits, underscores, and hyphens. The host stores each token in lowercase and does not interpret it. `default` is sent when it is listed; it does not mean unset. An empty or omitted list means this model has no service tier |
| `speeds` | Speed tokens this model accepts, using the same token rules. `fast` and `ultrafast` belong here when the endpoint accepts them. An empty or omitted list means this model has no speed |
| `imageInput` | Whether this model may receive images (default `false`; supported by all built-in protocols) |
| `audioInput` | Declared audio input (default `false`). Stored and visible to plugins. The host does not accept or send audio |
| `videoInput` | Declared video input (default `false`). Stored and visible to plugins. The host does not accept or send video |

`audioInput` and `videoInput` only record the claim. Crystal Code stores
them and shows them to plugins. It does not attach, paste, persist, or send
audio or video, and those flags do not register multimodal tools.
`imageInput` is still the only flag that allows image input.
Service tier and speed are sent only when the active model lists the
selected token. The host does not choose the JSON field and does not
compare the response with the request. Chat Completions and OpenAI
Responses write one set slot to `service_tier` and reject both when the
client is created, before the choice is saved. Anthropic Messages writes
`service_tier` and `speed` separately. DeepSeek, Gemini, and Ollama reject
either slot when the client is created. A saved combination the protocol
cannot send fails at startup. The endpoint address is not used to decide.

Gemini and Ollama native adapters accept inline images in user messages.
Tool images are sent on Gemini `functionResponse` parts and on the Ollama
tool message `images` array. Gemini tool images require a model that accepts
multimodal function responses.

`thinkingEffort` is a host setting, not a model field. Changing
models never fails: if the model does not support thinking, requests
omit reasoning hints; if the stored gear is not in that model's list,
the request uses the provider default and the stored choice is
unchanged. The host stores a listed token in lowercase and forwards
that text. Adapters send the token unchanged. `max` and `maximum` are
different tokens. `openai` and `responses` are also used for
third-party gateways such as OpenRouter, so a catalog value is not
renamed to `xhigh`. List `xhigh`, `max`, or `maximum` only when that
endpoint accepts that spelling. Gemini 3 uppercases the token as
`thinkingLevel`. Gemini 2.5 maps `minimal`, `low`, `medium`, and
`high` to thinking budgets and rejects any other token.
Anthropic requests summarized thinking text when thinking is on, unless
that endpoint sets `options.thinkingDisplay` to `omitted`.
An empty `thinkingEfforts` list is on/off only. When
`thinkingCanDisable` is false, Off is left out of the gear. A stored
Off choice uses the provider default and the status bar shows
`Think Default`.

### Example: add an OpenAI-compatible provider

Add this entry to `~/.crystal/providers.json`. Keep the secret out of this
file. Write it in `~/.crystal/credentials.json` under `openrouter`.

```json
{
  "openrouter": {
    "protocol": "openai",
    "baseUri": "https://openrouter.ai/api/v1/",
    "replayReasoningContent": true,
    "tokenLimit": "max_tokens",
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
```

`OPENROUTER_API_KEY` overrides that file when the process environment sets
it. Restart after editing `providers.json`. Select the model with
`/model openrouter anthropic/claude-sonnet-4`, or set `provider` and
`model` in `config.json`.
CLI `--provider` and `--model` override that selection for one run;
`/approval` (including `/approval model` and `/approval thinking`),
`/thinking`, `/context`, `/tier`, `/speed`, and `/model` write their values to `config.json`.
`/context` stores each model's selected budget in `contextSelections`.
`/tier` and `/speed` store each model's token in `tierSelections` and `speedSelections`.

### Example: add an Anthropic (Claude) provider

Anthropic Messages is a built-in protocol, but the starter catalog ships no
Anthropic entry, so Claude models are reached by adding one to
`~/.crystal/providers.json`. Write the key in
`~/.crystal/credentials.json` under `anthropic`.

```json
{
  "anthropic": {
    "protocol": "anthropic",
    "baseUri": "https://api.anthropic.com/v1/",
    "models": {
      "claude-sonnet-4": {
        "contextWindow": 200000,
        "maxTokens": 8192,
        "thinking": true,
        "thinkingEfforts": ["low", "medium", "high"],
        "imageInput": true
      }
    }
  }
}
```

`ANTHROPIC_API_KEY` overrides that file for one process. Restart after
editing `providers.json`, then select the model with
`/model anthropic claude-sonnet-4`, or set `provider` and `model` in
`config.json`. The same entry reaches any Anthropic Messages gateway:
change `baseUri` and the model id to match it. List the endpoint token
in `thinkingEfforts`, such as `max` or `xhigh`. The adapter sends that
token unchanged, and it asks for summarized thinking text whenever thinking is on. Set
`options.thinkingDisplay` to `omitted` on this endpoint to leave the
Anthropic `display` field off. `summarized` is the default and is not
written back. The name is valid only for the `anthropic` protocol. Omit
`temperature` and `topP` for models that reject sampling parameters.
Thinking tokens count toward `maxTokens`.

### Example: add OpenCode Zen protocol endpoints

One gateway can group models that use different wire protocols under one
provider name. Put this example in `providers.json`. The key for both
endpoints belongs in `credentials.json` under `opencode-zen`.
`OPENCODE_ZEN_API_KEY` overrides that file for one process.

```json
{
  "opencode-zen": [
    {
      "protocol": "responses",
      "baseUri": "https://opencode.ai/zen/v1/",
      "models": {
        "gpt-5.6-sol": {
          "contextWindow": 1050000,
          "maxTokens": 128000,
          "thinking": true,
          "thinkingEfforts": ["low", "medium", "high", "maximum"],
          "imageInput": true
        }
      }
    },
    {
      "protocol": "anthropic",
      "baseUri": "https://opencode.ai/zen/v1/",
      "models": {
        "claude-sonnet-5": {
          "contextWindow": 1000000,
          "maxTokens": 128000,
          "thinking": true,
          "thinkingEfforts": ["low", "medium", "high", "maximum"],
          "imageInput": true
        }
      }
    }
  ]
}
```

Add a `protocol: "openai"` object to the same array for Chat Completions
models. Each model id must be unique within a provider. Listed efforts
are sent as written. `maximum` is not rewritten to `xhigh` or `max`.

## Interactive session

For an OpenAI Chat Completions, DeepSeek, Responses, or Anthropic model
configured with `imageInput: true`, run `/attach <workspace-image-path>` and
enter the prompt, or press Ctrl+V to attach an image from the clipboard.
`audioInput` and `videoInput` do not attach audio or video.
Clipboard image paste uses Windows PowerShell on Windows, the built-in
`osascript` command on macOS, and `wl-paste` or `xclip` on Linux. If a reader
is unavailable, Crystal Code reports that separately from a clipboard with
no image. Windows supports raw PNG clipboard data and bitmap fallback; macOS
accepts PNG, JPEG, and GIF clipboard representations; Linux accepts advertised
PNG, JPEG, GIF, and WebP formats. Ctrl+V is handled before a following Enter
in the same key batch. The composer and transcript show
`[Image #N]`; pasted markers have a distinct color and behave as one editing
unit. Typing the same text does not attach an image. Raw image data is kept out
of rendered text.
PNG, JPEG, GIF, and WebP input is accepted up to 20 MiB per image. Optional
multimodal plugin tools may return generic Crystal `ImageContent`, which is
fed into the next model round. When `imageModel` is set, Plan and Work
include `describe_image`, which asks that vision model to describe a file
so a model that cannot see images can still read it. The question it sends
comes from `image.system.md` and `image.user.md`. A model that accepts
images also gets `view_image`, which returns the file itself. Screenshot
capture, device/browser/VM control, and coordinate protocols belong to
external plugins, not CrystalCode.

The default command opens an alternate-screen shell when stdout is a
TTY: transcript viewport, optional overlay, optional pinned todos,
optional progress row, status bar, and a multiline composer. Redirected
output stays sequential.

The status bar shows approval, thinking (when the selected model
supports it), `Tier <token>` and `Speed <token>` when those slots are
set, selected context budget (`Context 272K`), model, workspace,
context percent (`CTX`), token counts
(`IN` / `OUT`), a Title Case total when the bar has room, tool count,
and elapsed time. Named chrome labels are
Title Case; short status abbreviations are uppercase. Mode is Plan or
Work on the composer prompt, not repeated on the status bar. A
queued-follow-up count appears while items wait. While a turn runs, a
progress row sits above the status bar (`Awaiting Approval · 5s`,
`Running Command · 2m18s`, `Thinking · 1m16s · ~1.2k Tokens`,
`Retrying In 8s (Attempt 1)`, `Compacting`), prefixed with a spinner, and is
independent of the status-bar activity bullet. The spinner is braille.
Console output is UTF-8, so Windows does not replace those frames with `?`
or garble CJK text.
The `~N Tokens` estimate
appears only when `estimatedTokens` is on. When the session has todos, a
pinned `Todos` bar sits above that progress row (first four items; `/todos`
prints the full list). Session start and `/cd`
show `Loading Tools` on that row while operator tool sets load, after
the frame is already up.

Assistant text is rendered as markdown while it streams and after it
commits (headings, lists, fenced code, inline code and bold). User,
thinking, tool, result, and compaction-summary blocks are rounded panels. Tool names in
chrome are Title Case. Approval cards for edit and write show a short
`+` / `-` preview of the change.

### Result detail

Success result panels use three switches. `verboseResults` is the parent.
`verboseTools` and `verboseCommands` change the picture only while that
parent is on. None of the three hides a failure result, an approval
card, a thinking panel, or an ask overlay. They also do not hide the
Tool card. That card is the call itself (`Read`, `Grep`, `Edit`,
`Bash`, `Apply Patch`), drawn before the result.

`/verbose calls` shows or hides those Tool cards. It stays on unless
`verboseCalls` is `false` in `config.json`. A missing field keeps it
on. Off omits every Tool card already in this session, and turning it
on shows them again. Results are unchanged: a hidden call can still
leave an `Edited …` line or command output, and hiding results can
still leave the Tool card. Use both switches when you want neither.
`crystal run` does not read `verboseCalls`.

`/verbose results` turns the parent on or off, or toggles it when you
omit `on` and `off`. It stays on unless `verboseResults` is `false` in
`config.json`. A config that has no such field keeps the parent on, so
edit and bash results do not disappear until you turn it off. While it
is off, every success result is omitted: read, search, skill, edit,
write, `bash`, `bash_start`, `bash_status`, `bash_logs`, and `bash_stop`.
The tool and command switches remain stored. They do not draw a result
again until `/verbose results on`. Ctrl+G does not expand command output
in that state, because the panel is omitted rather than collapsed.
Cards already in this session follow the switch.

While the parent is on:

| `verboseTools` | `verboseCommands` | Success results |
| :--- | :--- | :--- |
| On | On | Full text |
| Off | On | Read, search, and skill results are omitted. Edit and write results stay. Command output stays in full |
| On | Off | Command output collapses to a hidden-line hint and the last line. Edit and write results stay. Other results stay in full |
| Off | Off | Both of the off rows above |

`/verbose tools` and Ctrl+O on an empty composer toggle tool detail.
`/verbose commands` and Ctrl+G toggle command detail. `/verbose` with
no argument prints all of these switches. `crystal run` does not read
`verboseResults`; its plain log still shortens long command output on
its own.

### Composer keys

| Key | Action |
| :--- | :--- |
| Enter | Submit when idle; queue a follow-up while a turn is running |
| Empty Enter while a follow-up is queued | Interrupt the turn, or compaction if that is still running, and send the queue now |
| Empty Enter while working, queue empty | Does nothing. The turn keeps going |
| Ctrl+J or `\` then Enter | Insert a newline |
| Backspace | Delete one character |
| Ctrl+W or Alt/Option+Backspace | Delete a word (Windows: Ctrl+Backspace) |
| Tab | Toggle Plan/Work, or complete a `/` command (and its argument after `/thinking`, `/context`, `/tier`, `/speed`, `/approval`, `/model`, `/tokens`, or `/verbose`) |
| Shift+Tab | Toggle Plan/Work |
| `?` on an empty composer | Show shortcuts and commands |
| Up / Down | Move through the composer; at the first or last row, browse prompt history. Slash-picker navigation while the picker is open. A recalled prompt that starts with `/` stays in history until you edit it |
| Escape | Clear the composer |
| Mouse wheel, PageUp / PageDown, Ctrl+Up/Down | Scroll the transcript |
| Ctrl+C before the model shows text, reasoning, a tool call, or a retry, with an empty composer and an empty follow-up queue | Cancel the turn and put that message back in the composer |
| Ctrl+C during a turn after that | Cancel the turn. The message stays |
| Ctrl+C at idle | Clear the composer |
| Ctrl+C twice on an empty composer | Exit |

`crystal run` does not put a message back. A trusted image marker that the
session no longer holds is left in the transcript, and that Ctrl+C only
cancels. A queued follow-up also keeps the message. So does a note, an
error, or a slash command that runs during the turn, such as `/status` or
`/help`. Text typed while that turn is stopping is kept after the returned
message.

The alternate screen enables bracketed paste and mouse reporting (1000
with SGR encoding 1006), turns alternate scroll off, and repeats those
modes while the session is open.
On Windows the console's quick edit is off for that time, so the wheel
reaches the transcript instead of the console host; the previous console
input mode returns when the session closes. The wheel scrolls the transcript on its own and
never becomes Up/Down, so prompt history stays on the arrow keys. While
mouse reporting is on, the terminal's own selection needs Shift held
while you drag (Option or Fn in some macOS terminals). Rows that arrive
while you are scrolled back do not move what you are reading; scroll to
the bottom to follow new output again. Pasted text drops control,
invisible, and bidirectional formatting characters. The frame repaints
when the terminal is resized. Escape sequences that are not a paste wrap
are not treated as paste. Overlay prompts (approval
and questions) keep using that same input loop, so scroll and resize
still work while they are open.

Question prompts support one or more tabbed questions, described choices,
single or multiple selection, and custom text answers. Custom input is enabled
by default. A single single-select answer submits immediately; multiple
questions and multiple selection use a Confirm tab. Use Up/Down or `J`/`K` to
move, Enter to select, Space to toggle multiple choices, Left/Right or Tab to
navigate questions, and Escape to dismiss the request. While a custom answer is
being edited, its text and cursor appear inside the question panel; Enter saves
it and Escape returns to the choices without changing the answer. When the
question panel is taller than the room on screen, the mouse wheel, PageUp,
PageDown, and Ctrl+Up/Down scroll the rows inside it; the title and border
stay visible. Moving the highlight, or the cursor while editing a custom
answer, brings that row back into view. A manual scroll stays until the
highlight or cursor moves. A scroll past either end, and those same keys while
the panel already fits, scroll the transcript.

### Follow-up queue

The composer stays open while a turn runs. Enter with text enqueues
a follow-up (FIFO). Queued items stay in a `Queued` panel above the
composer. The queue is sent when the current tool batch finishes or
when the turn (thinking or conversation) ends. Empty Enter while a
follow-up is queued interrupts the turn, or compaction if that is
still running, so the queue is sent now. Interrupt does not drop
queued text.

`/quit`, `/clear`, `/resume`, and `/fork` stop a busy turn before they run,
including when the composer submits them while other session work is still
on the stack. `/clear`, `/resume`, and `/fork` stay in the Queued panel and
run in the order they were typed. Each of those three drops any follow-up
text still waiting in the panel; the panel updates and does not keep it.
During compaction, `/clear`, `/resume`, and `/fork` are refused
instead, with the same kind of message as `/cd` while a turn is running.
Other commands that change the session, workspace, or model are refused for
that same reason. `/status`, `/help`, `/todos`, and other commands that only
report state or adjust the display run immediately. `/quit` still exits.

### One turn

1. Snapshot the transcript and current tool definitions.
2. Stream one chat request. Render deltas as they arrive.
3. Stop before tools when the model did not finish normally. Truncated
   output keeps the text and ends the turn. A content filter keeps the
   text and ends the turn. Any other unusual finish reason fails the
   turn. These stops do not run tools and do not compact.
4. If the candidate has tool calls, run the full batch through the
   executor (approval first).
5. Append exact tool results.
6. Repeat until there are no tool calls, a limit stops the turn, or
   you cancel. The host may compact before a model round when the
   estimated transcript or the last model-round usage is over budget;
   if compaction cannot reduce further, the turn stops.
7. After a completed turn, consider compaction from that last round
   and the estimated transcript. `/compact` summarizes earlier context
   immediately and prints the summary.

## Modes

These are product modes, not Crystal types. Switching replaces the
first system message and the tool catalog. The transcript is
otherwise the same conversation.

| Mode | Tools | Side effects |
| :--- | :--- | :--- |
| **Plan** | Built-in read, list, tree, glob, grep, search, todowrite, todoread, question, and skill when enabled, plus any external tools listed for Plan | No built-in edit, write, or bash. External Plan tools keep a Write + Workspace floor and still go through approval. |
| **Work** | Built-in read, list, tree, glob, grep, search, todowrite, todoread, question, and skill when enabled, plus edit, write, bash, `bash_start`, `bash_status`, `bash_logs`, and `bash_stop`, plus any external tools listed for Work | After approval |

Tab, Shift+Tab, or `/plan` toggles Plan and Work.

## Approval

Every side-effect tool call is classified before invocation.

Risk: Read, Write, Privileged, Forbidden.

Authority: Workspace, OutsideWorkspace, Network, PrivilegedEscalation.

Grant: Once, Session, Persistent.

| Mode | Behavior |
| :--- | :--- |
| **Default** | Workspace read auto-executes. Write, shell, and paths outside the workspace ask you. When Skills is enabled, any path in a Skills search directory auto-passes. |
| **Edit** | Workspace file changes for built-in `write` and `edit` pass without review. Shell, external tools, and paths outside the workspace still ask. |
| **Review** | Workspace file changes for built-in `write` and `edit` pass without review, same as Edit. Another model checks each remaining side-effect call, including `bash`, `bash_start`, `bash_stop`, and read, list, tree, glob, grep, search, write, and edit outside the workspace, and external Write. Skills search directories auto-pass when Skills is enabled. A bounded transcript excerpt is attached (first and latest user turns as anchors, then other user turns, then recent assistant and tool evidence). A compaction summary stands in for folded turns. Without that evidence the host asks you. Later user messages refine the task; a status question does not revoke earlier authorization. Answers given through the question tool count as user authorization. Allow executes unless the call is Forbidden or requires a forced prompt. Deny becomes model-visible rejection text. Ask, Forbidden-allow, and forced-prompt allow fall back to you. A transient reviewer request failure is retried per `approvalRetry` (five times by default); if it still fails you are asked. Review is not a grant and is not full pass-through. |
| **Audit** | The same reviewer and transcript rules as Review, but workspace `write` and `edit` are also checked. They do not auto-pass. |
| **Full** | Workspace-bounded, policy-allowed actions pass without review, including any loaded external tool that stays Write + Workspace. Forbidden, Privileged, outside-workspace, and forced-prompt calls never fully auto-pass. |

Do not name a mode `auto`. That word is ambiguous between review and
full pass-through. `/approval` with no argument cycles Default,
Edit, Review, Audit, and Full. `/approval review` (and the
other names) sets one mode and writes it to `config.json`. Legacy
values `autoedit`, `fullreview`, and `full-review` still parse.

The reviewing model is the session model unless you turn on a separate
approval model. `/approval model` shows `Approval model  Off`, or
`Approval model  On  openai  gpt-5.6-sol` when a provider and model are
stored; a stored non-default gear is appended as `·  Think High`.
`/approval model on` and `/approval model off` persist the
switch and leave a stored provider, model, and gear in place. `/approval model
<model>` or `/approval model <provider> <model>` selects a catalog model,
keeps the stored gear, and turns the switch on. The model must exist in
`providers.json`. A
missing credential or an unknown model is refused, and the previous
setting stays. While the switch is on, Review and Audit call that
model with its own credentials and its stored thinking gear.
`/approval thinking` cycles that gear, or sets one by name, and
persists it. It needs a stored or selected approval model and works
while the switch is off. A stored gear the model does
not offer falls back to the provider default, and `/approval thinking`
refuses `off` for a model that cannot disable thinking. Tab completes
`/approval model` from `on`, `off`, and the catalog, and `/approval thinking`
from that model's gear. `/model`
and the work thinking gear do not change it. Compaction stays on the
session model. While the switch is off, no separate client is created
and Review and Audit follow `/model`. Finish the current turn before
changing the approval model. `/status` adds Approval model and Approval
thinking rows only while the switch is on, and `/status full` lists the
effective `retry` and `approvalRetry` policies. The status bar does not.

When you are asked, the overlay uses a two-column field grid
(Status, Reason, Risk, Authority, and for review also Outcome plus
rationale):

| Key | Grant |
| :--- | :--- |
| Y, Enter, or 1 | Once |
| S or 2 | Session |
| A or 3 | Always (persistent) |
| N, Escape, or 4 | Deny |

Persistent grants are stored in `~/.crystal/permissions.json`. Session and
Persistent are offered only for ordinary Workspace Read/Write calls without a
forced prompt. OutsideWorkspace, Network, Privileged, Forbidden, and forced
prompts offer Once/Deny only; Session/Always keys are ignored. Existing grants
are checked against the current classification on every call and cannot
bypass an upgrade. A forced prompt still asks you after a reviewer Allow;
reviewer Deny rejects. `crystal run` denies any call that reaches this prompt.

When a call auto-passes (policy, remembered grant, or review allow),
the shell prints a panel with Status, Reason, Risk, and Authority,
plus the classifier summary. A review allow also prints Outcome, the
reviewer's risk and authorization, and the rationale. `/verbose approvals`
hides or shows policy and grant panels. `/verbose review` hides or shows
review allow panels. Each stays on unless its field is `false` in
`config.json`. Cards already printed in this session follow their own
switch. A saved `verboseApprovals` value of `false` no longer hides review
allow cards. Asking you still uses the overlay.

Shell classification treats `sudo`, destructive filesystem commands,
pipe-to-shell downloads, force-push, and credential-path writes as
Forbidden or Privileged. Forbidden never fully auto-passes. Review and Audit
may deny those calls or escalate them to you. Writes under `.ssh`,
`.gnupg`, or `~/.crystal/credentials.json` are Forbidden.

## Thinking

`/thinking` (alias `/think`) cycles the host gear, or sets one by
name: `off`, `none`, `default`, or an effort token the selected model
lists. `max` and `maximum` are different tokens. Tab completes the
argument from those efforts. The choice is written to `config.json`.

The approval model has its own gear. `/approval thinking` cycles it, or
sets one by name, restricted to that model's efforts, and stores it in
`approvalModel.thinkingEffort`. It applies while the approval-model
switch is on.

The compaction model has its own gear. `/compact thinking` cycles it, or
sets one by name, restricted to that model's efforts, and stores it in
`compactionModel.thinkingEffort`. It applies while the compaction-model
switch is on. While the switch is off, summary requests omit reasoning.

If the selected model does not support thinking, the command reports
that and does nothing. The status bar shows `Think Off`, or `Think`
plus the resolved gear when thinking is on. Models that cannot disable
thinking omit Off from Tab completion. `/thinking off` then reports
`The selected model cannot disable thinking.` and leaves the gear
unchanged.

`/verbose thinking` hides or shows the Thinking panel in the interactive
transcript. It stays on unless `verboseThinking` is `false` in
`config.json`. Cards already in this session, including ones restored
from a saved session, follow the switch. The gear, the progress row,
and `/export` stay. `crystal run --show-thinking` does not use this
setting.

Switchable thinking-chain compression is not available. It would, after
each thinking pass, ask a model to compress and summarize that thinking
according to a requirement. `/compact`, microcompaction, and
`/verbose thinking` do not do that.

## Context budgets

Add selectable budgets to a model entry in `providers.json`, then restart:

```json
"my-model": {
  "contextWindow": 1000000,
  "contextWindows": [272000, 1000000]
}
```

`/context` cycles the sorted budgets. `/context 272k`, `/context 1m`, and
`/context 272000` select a declared value; Tab completes the current model's
choices. Each model remembers its choice in `config.json.contextSelections`,
a nested `{"provider": {"model": tokens}}` map. A model without a saved valid
choice starts at its lowest budget. Switching models restores that model's
choice. Finish any turn or compaction before switching budgets.

`--context` selects one declared budget for the model chosen at launch, and
only when that model has more than one. It applies to this process and is
not written to `contextSelections`. Switching models uses the other model's
saved budget. A later `/context` still writes the operator's choice.

The selected budget drives compaction, `/status`, and CTX percentages.
Lowering it retains the full archive and checks whether compaction is needed
before the next model request. The default status bar displays `Context 272K`
beside thinking; custom lines can include `context-window`, which is also in
the default custom field order. These are local budgets: they do not enable
provider-specific long-context features or guarantee a billing threshold,
because token estimates and large tool results can exceed the budget.

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
- Automatically fetching model-list information from a provider is
  planned and is not available in this build.
- The new `provider` and `model` are written to `config.json`.

Switching models never fails because of thinking: unsupported thinking
is omitted, and an unsupported stored gear uses the provider default
without changing the stored choice. Reasoning history keeps working
too: an adapter replays provider reasoning state only in the format it
owns. A block it cannot replay keeps its readable text, as assistant
text or in the model's thinking field where the protocol has one, and
drops the state; a block without readable text is dropped.

## Slash commands

Type `/` to open the picker. Built-in verbs:

Plain Up/Down moves the picker selection and automatically scrolls the visible
candidate window. Recalling prompt history does not open the picker, so Up/Down
keep moving through history even when an entry starts with `/`. Editing that
entry opens the picker again. Tab accepts the selected completion. Enter also
accepts a partial selection; a complete command still submits with one Enter.
Submitting returns the transcript viewport to the latest output.

| Command | Aliases | Action |
| :--- | :--- | :--- |
| `/help` | `/h` | Shortcuts and commands |
| `/plan` | | Toggle Plan / Work |
| `/approval` | | Cycle or set `default`, `edit`, `review`, `audit`, `full`. `model` shows or sets the approval model; `thinking` cycles or sets its gear |
| `/attach` | | Attach an image from the workspace |
| `/thinking` | `/think`, `/effort` | Cycle or set the thinking gear |
| `/context` | | Cycle the model's local context budget, or select one with `272k`, `1m`, or an exact token count; refused during a turn or compaction |
| `/tier` | | Cycle the model's service-tier tokens, including not sending one, or select a listed token. `default` is sent when the model lists it. Refused during a turn or compaction |
| `/speed` | `/fast` | Cycle the model's speed tokens, including not sending one, or select a listed token. Refused during a turn or compaction |
| `/tokens` | | Toggle estimated progress tokens, or set `on` / `off` |
| `/verbose` | | Show or set tool, command, result, call, approval, review, or thinking detail: `tools`, `commands`, `results`, `calls`, `approvals`, `review`, `thinking`, then `on` or `off`. `results` is the parent of `tools` and `commands`. `calls` hides Tool cards and does not change results |
| `/model` | | List catalog models, or set `model` / `provider model` |
| `/promptset` | `/prompts` | List prompt sets and effective sources, enable one set, or `/prompts export [dir]` |
| `/promptattach` | | List prompt attachments, or `enable` / `disable` / `up` / `down` one name |
| `/status` | | Cumulative tokens and context progress with workspace, model, and options; `full` adds diagnostics |
| `/stats` | | Replaces the session frame with Overview, Tokens, and Tools panels, including a tool share bar. Supports `all`, `<Nd>`, and `tools <count>`. Esc or `q` restores the session |
| `/btw` | `/side` | Asks a side question from the committed transcript. The answer stays in a panel above the composer and is not saved. An answer taller than the panel scrolls inside it with the wheel, PageUp, PageDown, or Ctrl+Up/Down, and the title and border stay visible. While the model has not started, the panel shows the same spinner as the progress row. Once reasoning starts, the caption changes to Thinking until answer text arrives. An empty `/btw` reopens it. Esc, Enter, Space, or Ctrl+C closes it. Ctrl+C also cancels a side question that is still running and leaves the main turn running. Left and Right step through earlier answers. `x` clears them |
| `/statusline` | | Show custom status-line state; use `on`, `off`, `reset`, or an ordered field list |
| `/clear` | `/new` | Start a new conversation (new session id) |
| `/cd` | | Show the workspace, or set it to an existing directory (`~` is expanded). An untrusted git root or directory asks first; No stays here. The operator space does not ask. Changing the directory is refused while a turn is running. A successful change replaces session directory rules with the destination workspace's defaults |
| `/access` | | List session directory rules, or `add <path> read` / `write` and `remove <path>`. `default` lists this workspace's defaults in `~/.crystal/access.json`. `default add` and `default remove` change that file and the current session. Refused during a turn or compaction |
| `/space` | | Switch to the operator space. Creates `{home}/space` when it is missing. Takes no path. Refused while a turn is running |
| `/trust` | | Show workspace trust, or `on` / `off` / `forget`. `on` asks immediately when this root is not trusted; No exits. `forget` drops this root and takes effect on the next entry. `forget` leaves the operator space trusted |
| `/rules` | | Show global and project rule files, set both with `on` / `off`, or set one side with `global` or `project` and `on` / `off`. Writes `config.json`. Does not change `instructions.md` or `.crystal.md`. Refused during a turn or compaction |
| `/resume` | `/continue` | Choose a session in this workspace. `/resume <path>` lists that directory and enters it. `/resume all` lists every workspace and enters the chosen session's directory. `/resume <id>` loads that file and stays here |
| `/fork` | | Branch the current conversation, or `/fork <id>` to branch a saved session |
| `/sessions` | | List sessions for this workspace; `/sessions all` lists every workspace |
| `/compact` | `/summarize` | Summarize earlier context now and print the summary (refused while a turn is running). `model` shows or sets the compaction model; `thinking` cycles or sets its gear. `summary` toggles the manual summary; `auto` toggles the automatic one. `micro` toggles removal of old tool results. `on` or `off` sets any of those switches |
| `/export` | | Export markdown or json, or show usage; optional `[path]` and `--system` |
| `/todos` | `/todo` | Print the full session todo list (no `+N more` truncation) |
| `/tools` | | List grouped tool catalogs and configure external-tool loading and approval |
| `/quit` | `/exit`, `/q` | Exit |

`/export markdown [path] [--system]` and `/export json [path] [--system]`
write the current conversation under `exportDirectory` (default
`~/.crystal/exports/`). Markdown and JSON omit the live system prompt
unless `--system` is set. `/prompts export [dir]` writes built-in
prompt templates with placeholders. Quoted paths may contain spaces.
The slash picker completes `markdown` / `json`, offers `--system` before or
after an explicit export path, and exposes `export` plus directory examples
under `/prompts`.

Unknown `/` text prints `unknown command`. `/cd` with no argument
prints the current workspace root. `/cd` only accepts a directory
that already exists. `/tokens` with no argument toggles the live
progress-row estimate (four characters per token, prefixed with `~`).
It is not provider-billed usage. The choice is written to `config.json`.

`/statusline` does not replace `/status`. The status line is a configurable
live summary, while `/status` remains a stable diagnostic report. Supported
custom fields are `approval`, `thinking`, `tier`, `speed`, `context-window`, `prompt-set`, `activity`, `model`,
`workspace`, `context-used`, `context-left`, `context-tokens`,
`request-input`, `request-output`, `request-total`, `session-input`,
`session-output`, `session-total`, `tools`, `elapsed`, and `queued`.
A disk plugin may add more names. They do not appear until you put them in
this list. `/statusline` with no arguments lists built-in fields, then each
loaded plugin field with its directory and description, then any saved name
that is not loaded. `/statusline` rejects a name that is not built in and
not currently loaded.
Setting an ordered field list enables the custom line. `/statusline off`
restores the existing adaptive line; `/statusline reset` enables the built-in
custom field order.

## Built-in tools

`read`, `list`, `tree`, `glob`, `grep`, `search`, `edit`, and `write` may use a path outside the
workspace after approval (you, or the Review model in Review or
Audit). Workspace `write` and `edit` still follow the approval mode.
`bash` starts in the workspace root and is approved as a side effect.
Background Bash tasks are available in Work only. `bash_start` returns a
session-local task ID without waiting; use `bash_status` or `bash_logs` to
observe it and `bash_stop` to terminate it. The task uses the configured
`backgroundBashTimeoutSeconds` (default 10800 seconds, 3 hours), keeps bounded output, is
not persisted, and is stopped when the session closes. At most 32 tasks may be active at once; completed
tasks remain queryable without consuming an active-task slot. Background task
stdin is closed, so commands that require terminal input are unsupported.
Credential paths
(`.ssh`, `.gnupg`, `credentials.json`) stay Forbidden. Glob, grep, search, list, and tree
skip `.git`, `.vs`, `bin`, `obj`, `node_modules`, and `dist`. Tree lists
a directory link once, under its resolved path, and skips a link whose
target leaves the workspace root (or an outside start path). Binary
files are rejected for read/edit (NUL probe). Shell working directory
is the workspace root.

| Tool | Catalog | Purpose |
| :--- | :--- | :--- |
| `read` | Plan, Work | Read a workspace text file (`path`, optional 1-based `offset` and `limit`) |
| `describe_image` | Plan, Work | Describe an image through `imageModel` (`path`, optional `question`). Omitted when `imageModel` is unset |
| `view_image` | Plan, Work | Show an image to the current model (`path`). Present only while that model accepts images |
| `list` | Plan, Work | List a directory's immediate entries (`path` optional, 1-based `offset` and `limit` paging) |
| `tree` | Plan, Work | List the directory structure under a path (`path` optional, `depth` default 2 capped at 4, `files` to include file entries). Depth-first; subdirectories before files; up to 1,000 entries and 20 files per directory |
| `glob` | Plan, Work | List files matching a glob (`pattern`, optional `path`, 1-based `offset` and `limit` paging) |
| `grep` | Plan, Work | Regular-expression search (`pattern`, optional `path` and file-name `glob`) |
| `search` | Plan, Work | Ranked candidate files by name and content (`query`, optional `path`, `glob`, `caseSensitive`, 1-based `offset` and `limit` paging) |
| `todowrite` | Plan, Work | Replace or merge the session todo list |
| `todoread` | Plan, Work | Read the current session todo list |
| `question` | Plan, Work | Ask one or more questions with single/multiple choices and custom answers |
| `skill` | Plan, Work | Load an available skill by `name` (omitted when `skills` is `false`) |
| `edit` | Work | Replace one unique `old_string` in a file |
| `write` | Work | Create or overwrite a UTF-8 text file |
| `bash` | Work | Run one shell command after approval (`bash -lc`). Per-command timeout is `bashTimeoutSeconds` (default 600 seconds) |
| `bash_start` | Work | Start a session-local background shell command after approval and return its task ID. Timeout is `backgroundBashTimeoutSeconds` (default 10800 seconds, 3 hours) |
| `bash_status` | Work | Read the status, exit code, and log offsets of a background task |
| `bash_logs` | Work | Read bounded background task output by offset |
| `bash_stop` | Work | Stop a background task and its child processes after approval |

Practical limits: read up to 1,000,000 characters or 20,000 lines;
write up to 2 MiB; grep up to 1,000 matches and 8 MiB per file; search
up to 200 candidate files per page (default 50) with up to 2 preview
lines per file and 8 MiB per scanned file; glob
up to 1,000 matches per page; list up to 1,000 entries per page; tree up to
1,000 entries, 4 levels deep, and 20 files per directory; tool
output truncated at 100,000 characters. Background task output is capped at
1 MiB per task, and `bash_logs` returns at most 100,000 characters per call.

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
  root. Each child receives `CRYSTAL_WORKSPACE`, `CRYSTAL_SESSION`,
  `CRYSTAL_APPROVAL`, `CRYSTAL_TOOL_DATA` (the set's global data
  directory), and `CRYSTAL_PROJECT_DATA` (its project data directory).
  `"output": "content"` makes stdout a JSON object
  with `text` and optional `images` (`base64` or a workspace `path`).
  Text-only turns reject image results. Image-capable turns attach them
  through the same marker path as other tools.
- **dotnet**: a framework-dependent class library that implements
  `Crystal.Tools.ITool` or `Crystal.Multimodal.Tools.IMultimodalTool`.
  A tool that implements `CrystalCode.Tools.IHostTool` or
  `IHostMultimodalTool` receives a `ToolHostContext` on each call
  (workspace root, session id, approval mode, and the set's global and
  project data directories). Tools that do not
  implement those interfaces are unchanged. Every public non-abstract
  tool is loaded in one isolated load context for that set. Shared
  types are `Crystal`, `Crystal.Tools`, and `CrystalCode.Tools`; other
  dependencies stay private to the set.

Every external tool is at least Write + Workspace and still goes
through `ToolInvocationPolicy` by default. Authors may set `approval` to
`always`; Home tools follow that declaration by default, while Project tools
use host policy unless configured otherwise. A dotnet text or multimodal tool
may implement `IHostToolApprovalClassifier` when its actual write paths are
embedded in arguments; the host merges that side-effect-free result with its
own classification, the result can only raise risk or authority, and
classification failures fail closed. Classification receives cancellation and
a separate two-second budget, independent of the execution timeout. A timed-out
or cancelled classifier is disabled until the tools reload; later calls fail
classification closed. The host authors the summary rather than adopting tool
wording. `/tools` lists the effective
catalog and approval and marks tools that supply their own classifier,
`/tools home|project author|host` changes source trust,
and `/tools on|off|reload` controls discovery.
`/tools enable|disable|show <directory>` changes one tool set.
`crystal tools list|show|enable|disable` does the same outside a session
and prints a Spectre.Console table unless `--format text` is set.
Full auto-passes a
workspace-bounded external write; Edit does not. Details, manifest fields,
and the
dotnet publish layout are in
[External tools](external-tools.md).

## Plugins

Operators add in-process extensions under `plugins/`. A project directory
of the same name replaces the home plugin. `"enabled": false` in
`plugin.json` leaves the directory in place without loading it.
`"plugins": false` in `config.json` skips discovery. When `true`, the
field is omitted from the written file.

A plugin can add tools, a protocol client, approval classifiers, slash
commands, hooks, raw hooks, and prompt placeholders. It can read the
current session model and whether review is using its own model. A plugin
that implements `CrystalCode.Plugins.Data.IPluginDataDirectory` receives its
global and project runtime-data directories. A plugin
that asks for model clients can call the session model and, while that
switch is on, the review model. Those calls use separate clients from the
turn, and they do not enter the transcript. Hooks may append prompt and compaction text,
rewrite a user message before it is stored, revise the text of one outbound
model request without changing the stored transcript, read one completed
model response, rewrite a tool call (approval runs
again), replace a tool result, and raise approval risk. They cannot lower
risk or replace Work, Plan, or Review. A raw hook is a privileged
extension point and is not held to those limits. It may rebuild one
outbound model request, replace composed prompt and compaction text with
any string, and replace an approval classification with any risk,
authority, summary, and prompt requirement. A plugin that registers one is
named in a note at load time. More hook methods will be added later.
Whether plugins declare dependencies on one another is under consideration
and is not decided. A later module system may sit below plugins
and reach the host through reflection or another mechanism. It is not in
this build.
`/plugins` lists them. `/plugins on|off|reload` controls discovery.
`/plugins enable|disable|show <directory>` changes one plugin.
`crystal plugins list|show|enable|disable` does the same outside a session
and prints a Spectre.Console table unless `--format text` is set.
Details are in [Plugins](plugins.md).

## Prompts and instructions

Crystal is prompt-neutral. Every model-bound string this product
sends is authored here. Operators may replace Work, Plan, Review, the Review
user turn, topic naming, compaction, and image description
by placing files under `~/.crystal/prompts` and
`<workspace>/.crystal/prompts`.

Named files (`work.md`, `plan.md`, `review.md`, `review.user.md`, `topic.md`,
`compaction.system.md`, `compaction.user.md`, `image.system.md`, and
`image.user.md`; `.txt` is also accepted):

- Within direct overrides, the Home file is applied before the project file.
- A project file replaces the home file for that name.
- Empty files are treated as missing.
- The host never writes prompt files.

Prompt set selection is separate from direct prompt overrides. Reusable sets
live only under `~/.crystal/promptsets/<name>/`; the workspace is never scanned
for prompt sets. Each set contains `prompt.json` and any subset of those
named files (`.txt` is also accepted).
Missing or empty members use the built-in prompt for that name.

`prompt.json` may include `name` and `description`. `name` is a display title
of at most 80 characters. `description` is at most 280 characters. The
directory name remains the id used by commands. `enabled` turns the directory
on. Omitting `enabled` means off. Unknown fields are kept. A missing,
unreadable, or invalid file skips that directory. Enable and disable do not
create a directory or a missing file, and they leave a broken file unchanged.

At most one prompt set is enabled. Enabling one turns the others off. If more
than one set is hand-edited to `enabled: true`, Crystal uses the built-in
prompts and reports the conflict. `/promptset default` turns every set off.

Final precedence for each named prompt:

1. Built-in default.
2. Selected Home prompt set.
3. Direct `~/.crystal/prompts` override.
4. Direct `<workspace>/.crystal/prompts` override.

Use `/promptset` (alias `/prompts`) to list available sets and the effective
source of Work, Plan, and Review. `/promptset <name>` enables that set.
`/promptset default` returns to the virtual built-in selection. Switching is
refused while a turn runs, though listing remains available. A non-default
set appears as `Prompt <name>` in the status bar and as a startup note.
`crystal promptsets list|show|enable|disable` edits the same files outside a
session.

Older `promptSet` and `promptAttachments` values in `config.json` are ignored.
The next preference save omits them. There is no migration tool. Add
`prompt.json` to each directory you still want.

Prompt attachments append after the resolved Work, Plan, or Review text.
An attachment does not replace topic, Review user, compaction, or image
description. Each attachment is a directory
with `prompt.json` and any of `work.md`, `plan.md`, and `review.md` (`.txt`
is also accepted). A mode with no file gets nothing from that attachment.
Empty files are treated as missing. A trailing newline at the end of an
attachment file is kept. Directory names are 1-64 lowercase
alphanumeric words joined by single hyphens. `default` is allowed.

Attachments are discovered in both places:

1. `~/.crystal/prompt-attachments/<name>/`
2. `<workspace>/.crystal/prompt-attachments/<name>/`

The workspace scan is the current workspace root only. Parent directories are
not walked. When the same name exists in both places, the workspace
`prompt.json` replaces the Home file, including `enabled` and `order`.
Discovery does not enable a directory by itself.

Many attachments may be enabled. They append in `order`, then by directory
name. A missing `order` sorts last. Enabling a name places it last and
rewrites the enabled orders as `0`, `1`, `2`, and so on. Disabling sets
`enabled` to false and does not renumber. Placeholders inside an attachment
expand with the same values as the body.

`/promptattach` lists every discovered attachment. Effective rows are numbered
and show the display name, description, and Home or Workspace source.
`/promptattach enable <name>` and `disable <name>` edit the winning directory.
`/promptattach up <name>` and `down <name>` move a name in the enabled order.
Changes are refused while a turn is running. Listing stays available. The
status bar still shows only the selected prompt set. `crystal
prompt-attachments list|show|enable|disable` accepts `--home`, `--workspace`,
`--source home|project`, and `--format text`.

`crystal`, `crystal space`, and `crystal run` follow the enabled manifests.
`--prompt-set <name>` forces one
set for that process, including a set whose file says `enabled: false`.
`default` forces the built-in text. `--prompt-attachments off` appends
nothing and refuses `/promptattach` enable, disable, up, and down. Listing
stays available. Omitting the flag, or passing `on`, follows the files.
Repeat `--prompt-attachment <name>` to append that attachment after the
enabled set, even when its `prompt.json` says `enabled: false`. A name that
is already enabled stays in its file order. The same flag with
`--prompt-attachments off` is rejected, and a name that is not in Home or
the launch workspace fails startup. `/promptattach enable`, `disable`,
`up`, and `down` are refused for a forced name and do not write the file.
Other names can still be changed. Listing stays available. A later workspace
change that no longer contains a forced name skips it and prints a note.
`--prompt-set` refuses `/promptset` switching for the process, including
`default`, and does not write `prompt.json`. Listing and export stay
available. None of these flags write `prompt.json` or `config.json`.

Enabled attachment text is added after the body is bound, and before an
ordinary plugin prompt hook appends its own text.

The built-in Work and Plan assistant name is Crystal Code. Work, Plan,
Review, compaction, and image-description templates use host-owned placeholders (`{{name}}`).
Composite session slots are `{{env}}`, `{{skills}}`, `{{directories}}`, and
`{{instructions_section}}` or raw `{{instructions}}`. Atomic session slots
include `{{workspace}}`, `{{is_git_repo}}`, `{{git_root}}`, `{{platform}}`,
`{{os}}`, `{{architecture}}`, `{{date}}`, `{{time}}`, `{{provider}}`,
`{{model}}`, `{{model_line}}`, `{{mode}}`, `{{product_name}}`,
`{{session_id}}`, and `{{approval}}`. `{{os}}` is the operating system name
and version. `{{architecture}}` is the process architecture, such as `x64`
or `arm64`. `{{time}}` is the local time with a numeric offset, captured
when that system message is composed. `{{is_git_repo}}` is `yes` when the
workspace directory itself contains a `.git` directory or file.
`{{git_root}}` is the repository root found by walking parent directories,
or empty when that walk finds none. `{{session_id}}` is the saved session
id. `{{approval}}` is the approval mode (`plan`, `default`, `edit`,
`review`, `audit`, or `full`). `{{mode}}` is `plan` or `work` for Work and
Plan, `review` for Review system text, `compaction` for compaction
system text, and `image` for image-description system text. The `{{env}}` block includes the operating system,
architecture, and local time, and adds git root, session, and approval
lines when the host has them. `{{directories}}` is empty when the session
has no added directory rules. Otherwise it is the same session directory
access text appended to workspace instructions. Built-in templates do not
declare this slot. It refreshes when the live system message is replaced,
including after `/access`. These values refresh
on `/cd`, `/model`, `/approval`, and whenever the live system message is
replaced. Review user templates add `{{conversation}}`,
`{{tool_name}}`, `{{tool_arguments}}`, `{{host_risk}}`, `{{host_authority}}`,
and `{{classification_summary}}`. Compaction user templates add
`{{conversation}}`, `{{prior_summary_section}}`, `{{summary_task}}`,
`{{output_template}}`, and `{{todos_section}}`. Image user templates add
`{{question}}`, the description request or the model's question.
Placeholder names are
case-insensitive. Unknown names are left unchanged. A plugin may add
further names. While resolving a name, the plugin can read the current
session model and whether review is using its own model. The host rejects
a name it already owns, and inserts the plugin's value as text. Templates must declare
every host slot they need. When Skills is enabled, available-skill guidance
fills `{{skills}}`. Composite host values are not overlayable.

Workspace facts are appended under "Workspace instructions" on Work
and Plan only. Review does not receive that instruction block. Enabled
prompt attachments can still append to Review.

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

`ruleFiles` in `config.json` turns those compatible files on or off.
`global` and `project` each default to on and are omitted until off.
`/rules` shows them and writes the same file. `/rules on` or `off` sets
both sides. `/rules global off` or `/rules project off` sets one side
and leaves the other as it is, including a launch override that is not
on disk. `--rules` does the same for one process and is not written.
`off` skips that discovery. `instructions.md` and `.crystal.md` stay.

## Skills

Skills are OpenCode-compatible instruction folders. They are not
prompt overlays. The model sees a list of available skills and loads
one with the `skill` tool. When Skills is enabled, `read`/`list`/`tree`/`glob`/`grep`/`search`
of any path inside a Skills search directory (`skill` / `skills`
trees, including scripts and other files that are not `SKILL.md`)
auto-passes; it does not ask you. Set `"skills": false` in
`config.json` to disable the tool, the guidance, and that auto-pass.
Other files outside the workspace still need approval (you, or the
Review model in Review or Audit).

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

`/cd` and `/space` reload skills and external tool sets from the new
workspace. Both, and a resume that enters another directory, reload prompts
from that workspace. Resume refreshes the first system message from the current
prompt files, the current `<env>` block, and current skill guidance.

## Sessions

Sessions are written to `~/.crystal/sessions/<id>.json` after each
completed turn, and again on an orderly exit when the transcript has
a user message. The file stores the transcript, todos, last-request and
cumulative provider-reported token usage, and turn counts.

`/quit` and two Ctrl+C presses on an empty composer leave the
alternate screen, then print the session id and `crystal --resume <id>`.
`--resume` without a value opens a selector for this workspace before the
alternate screen. `--resume <id>` loads that file and stays in the process
workspace. `--resume <path>` lists that directory and, after a session is
chosen, enters it. `--resume all` lists every workspace and enters the
chosen session's directory. `--workspace` and `--resume <path>` must name
the same directory when both are set. A missing or empty session exits
without entering the TTY. A redirected terminal cannot open the selector
and must pass `--resume <id>`.

`/resume`, `/resume <path>`, and `/resume all` use those same rules inside
a running session. Trust is confirmed before a named directory's selector
opens. Escape leaves the current workspace unchanged. Choosing a session
from `/resume all` asks for trust before entering that session's directory.
Declining trust does not restore the session. `/resume <id>` restores that
transcript and stays in the current workspace.

| Command | Effect |
| :--- | :--- |
| `crystal --resume` | Choose a session for this workspace at process start |
| `crystal --resume <id>` | Load that file under `~/.crystal/sessions` and stay in this workspace |
| `crystal --resume <path>` | Choose a session from that directory, then enter it |
| `crystal --resume all` | Choose a session from any workspace, then enter its directory |
| `/resume` | Choose a session for this workspace and replay the transcript |
| `/resume <path>` | Choose a session from that directory, then enter it |
| `/resume all` | Choose a session from any workspace, then enter its directory |
| `/resume <id>` | Load that file under `~/.crystal/sessions` and stay in this workspace |
| `/fork` | Save the current session and continue from an independent new id |
| `/fork <id>` | Branch that saved session into a new id in the current workspace |
| `/sessions` | List resumable sessions for the current workspace, newest first |
| `/sessions all` | List resumable sessions from every workspace, newest first |
| `/clear` | Start a new id |

Resume also restores the last usage snapshot and cumulative usage so the
status bar, `/status`, and compaction have the correct baselines before the
next model call. Older session files without cumulative usage show an unknown
cumulative value rather than a partial total. A compacted
session restores the full conversation on screen. The model continues from
the summary and recent tail, and only the live system prompt is refreshed.

Fork preserves the full conversation, the model context, todos, Plan/Work mode,
turn counters, and usage baseline. It does not copy directory access. The new
branch receives the current workspace's defaults from `~/.crystal/access.json`.
The source session remains unchanged. The
new branch receives a new creation time, uses the current workspace, and
refreshes its live system prompt. Session lists mark the current id and retain
complete ids so they can be copied into `/resume` or `/fork`.

## Compaction

Crystal does not reduce context. Before each model call, microcompaction
can replace old successful tool results with `Tool result omitted after
compaction.` It is off unless `microCompaction` is set. The latest
`protectTurns` user turns are kept, then the newest `protectTokens` of
older successful results. Anything older is replaced only when the
estimated size of the removed text is more than `minimumTokens`. It does
not call a model, and it does not change
the screen or the saved archive. `/compact micro` toggles it or sets `on`
or `off`, writes `config.json`, and does not summarize. It is refused
while a turn or compaction is running. `/status` shows a Microcompaction
row only while it is on.

When estimated transcript size or the last model-round usage crosses the
lesser of `compactionThreshold` of the model context window and the usable
window (context minus reserved output), the host:

1. Clears old tool results outside a protected recent band, when that
   frees enough tokens. A summary that stops because it reached its output
   limit is not stored. The summary request reserves 16384 tokens, or the
   session model's `maxTokens` when that is smaller, and never more than
   half the summary window. The verbatim tail is at most 32000 tokens.
2. Asks the model for a structured summary of older turns (folding any
   previous summary) and keeps a recent tail verbatim.
3. Stops if the summary cannot be produced and nothing else can be
   reduced. Compaction does not loop.

CTX percent is the last model round, not the sum of rounds in a turn.
The transcript prints `compacting context...` and the progress row
shows `Compacting` while this runs; the frame keeps painting so the
terminal does not freeze. `/compact` (alias `/summarize`) runs
immediately and does not wait for the threshold. When the retained tail
already holds every turn, it still summarizes everything before the latest
user turn. A session with nothing before that turn prints
`Nothing earlier to compact`. It is refused while a turn is running.
`/compact model` shows `Compaction model  Off` or `On  <provider>  <model>`,
turns that switch on or off, or selects `<model>` or `<provider> <model>`
and turns it on. `/compact thinking` cycles or sets that model's gear.
Both are refused during a turn or compaction. `/compact summary` toggles
`showCompactionSummary` and prints `Compaction summary  On` or `Off`.
`/compact summary on` or `off` sets it. `/compact auto` does the same for
`showAutomaticCompactionSummary` and prints `Automatic compaction summary`.
`automatic` is the same command. These display commands write `config.json`
and do not summarize. Any other argument is rejected and does not summarize.
Tab completes `model`, `thinking`, `summary`, `auto`, and `micro`, then `on` and `off`.
When the compaction-model switch is off, or `compactionModel` is omitted, summaries use
the session model and the request omits reasoning. When it is on, summary
requests use a separate client for that provider and model, including
`/compact` and the summary a side question may take before it answers.
The side answer still uses the session model. Changing the session model
does not retarget an open compaction client. The trigger, retained tail,
and CTX percent stay on the session window. The summary itself must fit
the smaller of the session window and the compaction model's declared
window; if it does not, the host keeps the existing "no summary" path.
`/status` adds a Compaction model row only while the switch is on.
A successful compact is written to the session file. The screen keeps the
earlier lines. When `showCompactionSummary` is on (the default), a manual
`/compact` prints the summary under those lines in a rounded panel titled
`Earlier context`. Automatic compaction prints `Compacted context` and, when
`showAutomaticCompactionSummary` is on, the same panel. That switch defaults
off. `/resume` restores that full conversation, while the model continues
from the summary and tail, and refreshes only the live system prompt.

## Data directory

Override with `CRYSTAL_HOME` or `--home`.

```text
~/.crystal/
  binaries/code/CrystalCode (CrystalCode.exe on Windows)
  config.json
  providers.json
  credentials.json
  permissions.json
  trusted.json
  access.json
  instructions.md
  AGENTS.md
  prompts/
    work.md
    plan.md
    review.md
    review.user.md
    topic.md
    compaction.system.md
    compaction.user.md
    image.system.md
    image.user.md
  promptsets/
    concise/
      prompt.json
      work.md
      plan.md
      review.md
      review.user.md
      topic.md
      compaction.system.md
      compaction.user.md
      image.system.md
      image.user.md
  prompt-attachments/
    <name>/
      prompt.json
      work.md
      plan.md
      review.md
  skill/<name>/SKILL.md
  skills/<name>/SKILL.md
  tools/<directory>/tools.json
  data/tools/<directory>/
  plugins/<directory>/plugin.json
  data/plugins/<directory>/
  sessions/<id>.json
  logs/
```

Project overlay (named prompts, prompt attachments, Crystal skills, tool
sets, and plugins of the same directory name win over home):

```text
<workspace>/.crystal/
  instructions.md
  prompts/
    work.md
    plan.md
    review.md
    review.user.md
    topic.md
    compaction.system.md
    compaction.user.md
    image.system.md
    image.user.md
  prompt-attachments/
    <name>/
      prompt.json
      work.md
      plan.md
      review.md
  skill/<name>/SKILL.md
  skills/<name>/SKILL.md
  tools/<directory>/tools.json
  plugins/<directory>/plugin.json
  data/tools/<directory>/
  data/plugins/<directory>/
<workspace>/.crystal.md
<workspace>/AGENTS.md
```

`config.json` is not part of the project overlay; workspace-level
configuration is deferred. `access.json` is not that file. It stores
default extra directories per canonical workspace root. A new session,
`/clear`, `/fork`, and a successful `/cd` or `/space` copy those defaults
into the session. `/resume` keeps the saved session rules and does not
add defaults back. `--allow-read` and `--allow-write`, when any is set,
replace the defaults for that process and are not saved. A missing
directory or a credential path is skipped with an error and left in the
file. A corrupt file or two modes for one directory seeds nothing. The
session still opens.

Operator plugins and external tool sets keep their own runtime data under a
`data/` root, split by kind. The global root is `~/.crystal/data`; the
project root is `<workspace>/.crystal/data`. Each extension gets
`{home}/data/{plugins|tools}/<directory>` (global, shared across workspaces)
and `<workspace>/.crystal/data/{plugins|tools}/<directory>` (project). The
host creates both when the extension first receives them, at plugin attach
or before a tool call. They are separate from the `tools/` and `plugins/`
install trees and are not a secret store.

`plugins/<directory>/plugin.json` loads one operator plugin. Dotnet tool
sets still load class libraries from the tool-set directory only. See
[Plugins](plugins.md).

## Environment variables

| Variable | Meaning |
| :--- | :--- |
| `CRYSTAL_HOME` | Data directory instead of `~/.crystal` |
| `DEEPSEEK_API_KEY` | DeepSeek key for this process (overrides `credentials.json`) |
| `OPENAI_API_KEY` | OpenAI key for this process (overrides `credentials.json`) |
| `<PROVIDER>_API_KEY` | Key for a named provider (hyphens become underscores) |
| `CRYSTAL_API_KEY` | Shared key for this process when no provider-specific variable is set |
| `XDG_CONFIG_HOME` | Base for the global OpenCode `AGENTS.md` fallback |

Provider key variables override `credentials.json` for the process that
receives them. A lasting install stores the key in that file. A single
launch or a CI runner sets the variable. Do not pass secrets on the
command line. They appear in process lists.

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

- [README.md](../README.md) — project overview and quick start
- [Crystal](https://github.com/YELANDAOKONG/Crystal) — sibling library this product consumes
- [BUSINESS.md](../BUSINESS.md) — product boundary
- [ARCHITECTURE.md](../ARCHITECTURE.md) — ownership and runtime
- [STANDARDS.md](../STANDARDS.md) — engineering rules
- [AGENTS.md](../AGENTS.md) — instructions for coding agents
- [external-tools.md](external-tools.md) — operator tool sets
- [plugins.md](plugins.md) — operator plugins and hooks
