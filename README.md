# CrystalHarness

A production coding harness for local repositories. It runs a streaming
model-and-tool loop in the terminal, with Plan/Work modes, risk-aware
approval, automatic context compaction, and data under `~/.crystal`.

CrystalHarness consumes the Crystal library. It does not modify Crystal.

## Requirements

- .NET 10
- A sibling checkout of Crystal at `../Crystal`
- An API key for the selected provider: `providers.<name>.apiKey` in
  `config.json` (literal, `{env:NAME}`, or `{file:path}`), `{NAME}_API_KEY`,
  `CRYSTAL_API_KEY`, or `~/.crystal/credentials.json`

## Build

```bash
dotnet build CrystalHarness.sln
dotnet test CrystalHarness.sln
```

## Run

From a workspace you want the agent to edit:

```bash
dotnet run --project CrystalHarness -- --provider deepseek --model deepseek-v4-flash
```

The default command opens an interactive session in the current workspace.
Override the data directory with `CRYSTAL_HOME` or `--home`. Add
OpenAI-compatible providers and per-model `contextWindow`, `temperature`,
`topP`, and `maxTokens` in `config.json`.

## Modes

- **Plan** inspects the workspace. It cannot edit files or run a shell.
- **Work** can edit, write, and run commands after approval.

Tab or `/plan` switches Plan and Work. `/approval` switches Default,
AutoEdit, Review (another model checks safety and the user request), and
Full (pass without review). `/resume` loads the latest session for this
workspace, or `/resume <id>` a specific file under `~/.crystal/sessions`.

## Data

```text
~/.crystal/config.json
~/.crystal/credentials.json
~/.crystal/permissions.json
~/.crystal/instructions.md
~/.crystal/prompts/
~/.crystal/sessions/
~/.crystal/logs/
<workspace>/.crystal/instructions.md
<workspace>/.crystal/prompts/
<workspace>/.crystal.md
<workspace>/AGENTS.md
```

Work, Plan, and Review system prompts are files under `prompts/`
(`work.md`, `plan.md`, `review.md`; `.txt` is also accepted). Project
files override home files. `instructions.md` and `.crystal.md` append
workspace facts to Work and Plan. Review is not given those extras.

`AGENTS.md` and `CLAUDE.md` are extra instructions, not prompt
replacements. OpenCode rules apply: files are combined; in one
directory `AGENTS.md` is used instead of `CLAUDE.md`; the walk from
the workspace to the git root keeps every `AGENTS.md` (or every
`CLAUDE.md` when no `AGENTS.md` exists). Global fallbacks are
`~/.crystal/AGENTS.md`, `~/.config/opencode/AGENTS.md`, and
`~/.claude/CLAUDE.md`.

## Documents

- [BUSINESS.md](BUSINESS.md) — product boundary
- [ARCHITECTURE.md](ARCHITECTURE.md) — ownership and runtime
- [STANDARDS.md](STANDARDS.md) — engineering rules
- [AGENTS.md](AGENTS.md) — instructions for coding agents
