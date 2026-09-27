# Crystal Code

**A coding agent for your terminal, built on .NET and Crystal.**

Crystal Code works in a local repository through a streaming terminal UI. Ask it to inspect code, plan a change, or edit files. Plan and Work modes, tool approvals, and workspace boundaries keep you in control of what it can do.

[Get started](#get-started) · [Features](#features) · [Configuration](#configuration) · [User guide](docs/user-guide.md)

## Features

- **Stay in the terminal.** Follow streamed responses and tool calls, edit a multiline prompt, and queue follow-up requests while a turn runs.
- **Choose how work happens.** Plan mode offers built-in reading and planning tools; Work mode also enables file edits and shell commands. Tool calls follow a risk-aware approval policy.
- **Keep a conversation going.** Resume or fork saved sessions, switch models with `/model`, and compact older context automatically or with `/compact`.
- **Use the model endpoints you prefer.** Built-in DeepSeek and OpenAI catalogs are available, and you can configure OpenAI-compatible Chat Completions, OpenAI Responses, and Anthropic Messages endpoints.
- **Extend the workflow.** Load skills and operator tool sets from your home directory or workspace. Image-capable models can receive workspace or clipboard images.

Crystal Code is the coding product built on the sibling Crystal library. The terminal is its operator surface.

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

The installer places the application under `~/.crystal/binaries/code/` and adds it to your shell path. Open a new terminal after installation. To inspect a script before running it, download it first and run the local copy.

### 2. Set an API key

For the default DeepSeek provider, set `DEEPSEEK_API_KEY` in the environment of the terminal that will run Crystal Code. For OpenAI, use `OPENAI_API_KEY`. Other providers can use `<PROVIDER>_API_KEY` or a configured key reference. See [Credentials](docs/user-guide.md#credentials) for the full lookup order.

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
| `/resume` | Continue the latest session for this workspace |
| `/fork` | Branch the current conversation into a new session |
| `/compact` | Summarize older context now |
| `/help` | Show all commands and shortcuts |

See the [complete command reference](docs/user-guide.md#slash-commands).

## Configuration

Preferences and provider definitions are separate:

| File | Purpose |
| :--- | :--- |
| `~/.crystal/config.json` | Active provider and model, approval mode, and session preferences |
| `~/.crystal/providers.json` | Additional provider endpoints and model definitions |
| `~/.crystal/credentials.json` | Local API keys, if you do not use environment variables |

The built-in catalog includes DeepSeek and OpenAI. You can select any model listed in the effective catalog with `/model`, or use `--provider` and `--model` when starting the app. [Provider configuration](docs/user-guide.md#configuration) explains protocols, model fields, and examples.

File edits and shell commands stay within the workspace. Reads outside it require approval, and credential paths are forbidden. [Approval modes](docs/user-guide.md#approval) explain which calls ask you, use a reviewing model, or pass automatically.

## Build from source

You need the .NET 10 SDK and a sibling checkout of Crystal at `../Crystal`.

```bash
dotnet build CrystalCode.sln
dotnet test CrystalCode.sln
dotnet run --project CrystalCode -- --provider deepseek --model deepseek-flash
```

Run these commands from the Crystal Code repository root. To work on a
different repository, add `--workspace <path>` after the final `--` in the
`dotnet run` command. A TTY is required for the interactive UI, and `bash`
must be on the path (Git Bash is used on Windows when available).

## Documentation

- [User guide](docs/user-guide.md): installation, configuration, commands, tools, sessions, and prompts
- [External tools](docs/external-tools.md): tool-set format and runners
- [Product definition](BUSINESS.md): scope and terminology
- [Architecture](ARCHITECTURE.md): components and runtime behavior
- [Engineering standards](STANDARDS.md): source and verification rules
- [Agent instructions](AGENTS.md): repository guidance for coding agents

MCP servers, a headless CI runner, an operating-system sandbox, parent/child agents, audio and video input, and non-text model output are planned but not yet implemented. Image input is available for supported models and providers.

## License

[MIT](LICENSE)
