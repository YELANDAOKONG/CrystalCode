# CrystalHarness

A production coding harness for local repositories. It runs a streaming
model-and-tool loop in the terminal, with Plan/Work modes, risk-aware
approval, automatic context compaction, and data under `~/.crystal`.

CrystalHarness consumes the Crystal library. It does not modify Crystal.

## Requirements

- .NET 10
- A sibling checkout of Crystal at `../Crystal`
- A DeepSeek or OpenAI API key in `CRYSTAL_API_KEY` / `OPENAI_API_KEY` or
  `~/.crystal/credentials.json`

## Build

```bash
dotnet build CrystalHarness.sln
dotnet test CrystalHarness.sln
```

## Run

From a workspace you want the agent to edit:

```bash
dotnet run --project CrystalHarness
```

The default command opens an interactive session in the current directory.

## Modes

- **Plan** inspects the workspace. It cannot edit files or run a shell.
- **Work** can edit, write, and run commands after approval.

Tab or `/plan` switches modes. `/approval` switches Default, AutoEdit, and
Auto.

## Data

```text
~/.crystal/config.json
~/.crystal/credentials.json
~/.crystal/permissions.json
~/.crystal/sessions/
~/.crystal/logs/
```

## Documents

- [BUSINESS.md](BUSINESS.md) — product boundary
- [ARCHITECTURE.md](ARCHITECTURE.md) — ownership and runtime
- [STANDARDS.md](STANDARDS.md) — engineering rules
- [AGENTS.md](AGENTS.md) — instructions for coding agents
