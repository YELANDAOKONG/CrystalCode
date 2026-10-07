# Crystal Code

**A coding agent for your terminal, built on .NET and Crystal.**

Crystal Code works in a local repository through a streaming terminal UI. Ask it to inspect code, plan a change, or edit files. Plan and Work modes, tool approvals, and workspace boundaries keep you in control of what it can do.

[Get started](#get-started) · [Features](#features) · [Command line](#command-line) · [Configuration](#configuration) · [User guide](docs/user-guide.md)

## Features

- **Stay in the terminal.** Follow streamed responses and tool calls, edit a multiline prompt, and queue follow-up requests while a turn runs.
- **Choose how work happens.** Plan mode offers built-in reading and planning tools; Work mode also enables file edits and shell commands. Tool calls follow a risk-aware approval policy.
- **Keep a conversation going.** Resume or fork saved sessions, switch models with `/model`, and compact older context automatically or with `/compact`.
- **Use the model endpoints you prefer.** Built-in DeepSeek, OpenAI, Gemini, and Ollama providers are available, and you can configure OpenAI-compatible Chat Completions, OpenAI Responses, and Anthropic Messages endpoints.
- **Extend the workflow.** Load skills and operator tool sets from your home directory or workspace. Image-capable models can receive workspace or clipboard images.

Crystal Code is the coding product built on the sibling [Crystal](https://github.com/YELANDAOKONG/Crystal) library. The terminal is its operator surface.

## Get started

### 1. Install

The self-contained release supports Linux x64 and ARM64, macOS ARM64, and Windows x64.

**Linux or macOS**

```bash
curl --fail --location --show-error \
  https://raw.githubusercontent.com/YELANDAOKONG/CrystalCode/master/scripts/install.sh | sh
```

**Windows PowerShell**

```powershell
Invoke-RestMethod `
  -Uri https://raw.githubusercontent.com/YELANDAOKONG/CrystalCode/master/scripts/install.ps1 | Invoke-Expression
```

The installer places the application under `~/.crystal/binaries/code/`, adds that directory to your shell path, and aliases `crystal` to `CrystalCode`. Open a new terminal after installation. To inspect a script before running it, download it first and run the local copy.

### 2. Set an API key

Write the key in `~/.crystal/credentials.json` under the provider name. For the default DeepSeek provider the file looks like:

```json
{
  "deepseek": { "apiKey": "" }
}
```

The value is plain text. Where the operating system allows it, the file is limited to the owner.

`DEEPSEEK_API_KEY`, `OPENAI_API_KEY`, or `<PROVIDER>_API_KEY` overrides that file for the process that receives it. Use an environment variable for a single launch or a CI runner. See [Credentials](docs/user-guide.md#credentials) for the full lookup order.

### 3. Start in your repository

```bash
cd /path/to/your/repository
crystal
```

On Windows, run `CrystalCode` instead of `crystal`. The current directory becomes the workspace. The first run creates `~/.crystal/config.json`; the default selection is DeepSeek's `deepseek-flash` model.

Inside the app, type `/help` for commands and shortcuts. Use Tab or `/plan` to switch between Plan and Work. The [interactive session guide](docs/user-guide.md#interactive-session) covers the composer, image attachments, and follow-up queue.

## Everyday commands

| Command | What it does |
| :--- | :--- |
| `/plan` | Switch between Plan and Work |
| `/model` | List or select a configured provider and model |
| `/approval` | View or change the approval mode |
| `/attach <path>` | Attach a supported image from the workspace |
| `/resume` | Choose a saved session for this workspace, another directory, or every workspace |
| `/fork` | Branch the current conversation into a new session |
| `/compact` | Summarize older context now |
| `/help` | Show all commands and shortcuts |

See the [complete command reference](docs/user-guide.md#slash-commands).

## Command line

The interactive terminal is one entry point. Other commands start, do their work, and exit without a session:

| Command | What it does |
| :--- | :--- |
| `crystal run <task>` | Run one task without a terminal and exit. Anything that would ask the operator is denied. Accepts process-only overrides such as `--provider`, `--model`, `--workspace`, and `--approval`; they are not written to `config.json` |
| `crystal space` | Open the terminal in the operator space at `~/.crystal/space` |
| `crystal version` | Print the build identity and exit |
| `crystal plugins` | List, show, enable, or disable one operator plugin outside a session |
| `crystal tools` | List, show, enable, or disable one operator tool set outside a session |

See the [headless run](docs/user-guide.md#headless-run) and [operator space](docs/user-guide.md#operator-space) sections of the user guide for the full option list.

## Configuration

Preferences and provider definitions are separate:

| File | Purpose |
| :--- | :--- |
| `~/.crystal/config.json` | Active provider and model, approval mode, and session preferences |
| `~/.crystal/providers.json` | Additional provider endpoints and model definitions |
| `~/.crystal/credentials.json` | Plain-text API keys, one entry per provider |

The built-in catalog includes DeepSeek, OpenAI, Gemini, and Ollama. You can select any model listed in the effective catalog with `/model`, or use `--provider` and `--model` when starting the app. [Provider configuration](docs/user-guide.md#configuration) explains protocols, model fields, and examples.

File edits and shell commands stay within the workspace. Reads outside it require approval, and credential paths are forbidden. [Approval modes](docs/user-guide.md#approval) explain which calls ask you, use a reviewing model, or pass automatically.

## Build from source

You need the .NET 10 or .NET 11 SDK and a sibling checkout of [Crystal](https://github.com/YELANDAOKONG/Crystal) at `../Crystal`.

```bash
dotnet build CrystalCode.sln
dotnet test CrystalCode.sln
dotnet run --project CrystalCode -- --provider deepseek --model deepseek-flash
```

To install this checkout as `crystal`:

```bash
sh scripts/install-local.sh
```

On Windows:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install-local.ps1
```

The script checks for the .NET 10 or .NET 11 SDK, the sibling Crystal checkout, and the current platform, then publishes Release into `build/` with the release workflow arguments (`--self-contained true`, `-p:PublishSingleFile=true`, and the matching runtime). It replaces `~/.crystal/binaries/code/` and leaves configuration, credentials, and prompts in place. The script locates the repository from its own path.

Run `dotnet build`, `dotnet test`, and `dotnet run` from the Crystal Code repository root. To work on a different repository, add `--workspace <path>` after the final `--` in the `dotnet run` command. A TTY is required for the interactive UI, and `bash` must be on the path (Git Bash is used on Windows when available).

## Contributing

Maintainers can write a commit message and commit the current change with [`commit.sh`](commit.sh). It runs `crystal run` in this repository. Extra arguments are appended to the task. The run skips external tools and the directory trust check, and it does not record the directory as trusted.

## Documentation

- [User guide](docs/user-guide.md): installation, configuration, commands, tools, sessions, and prompts
- [External tools](docs/external-tools.md): tool-set format and runners
- [Plugins](docs/plugins.md): plugin contract, tools, and hooks
- [Product definition](BUSINESS.md): scope and terminology
- [Architecture](ARCHITECTURE.md): components and runtime behavior
- [Engineering standards](STANDARDS.md): source and verification rules
- [Agent instructions](AGENTS.md): repository guidance for coding agents

## Roadmap

MCP servers, an operating-system sandbox, parent/child agents, audio and video input, non-text model output, and editing provider definitions in the TUI are planned but not yet implemented. Image input is available for supported models and providers. `/model` selects a configured provider and model; it does not edit the catalog.

## License

[MIT](LICENSE)
