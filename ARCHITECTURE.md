# Architecture

## Status

This document is the authoritative architecture for the current coding
product. Public names may change while the product has no compatibility
baseline.

## Dependency direction

```text
CrystalHarness
    ↓
CrystalHarness.Providers
    ↓
Crystal

CrystalHarness also references Crystal.Tools, Crystal.Agents, and
Crystal.Harness directly. CrystalHarness.Providers references only Crystal.
No project in this repository modifies Crystal.
```

CrystalHarness.Tests references CrystalHarness.
CrystalHarness.Providers.Tests references CrystalHarness.Providers.

## Assembly ownership

### CrystalHarness

The executable host. Owns CLI commands, the terminal display, session and
turn execution, Plan/Work catalogs, approval policy, context compaction,
`~/.crystal` storage, built-in coding tools, prompts, and in-process plugin
contracts.

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

| Folder | Owns |
| :--- | :--- |
| `Commands` | Spectre.Console.Cli commands |
| `Configuration` | Loaded options and defaults |
| `Home` | `~/.crystal` paths and file I/O |
| `Display` | Terminal chrome, editor, markdown, approval cards |
| `Sessions` | Transcript, ledger, streaming turn, slash commands |
| `Approvals` | Risk, authority, grants, policy |
| `Compaction` | Window accounting and summary substitution |
| `Tools` | Workspace fence and built-in `ITool` types |
| `Prompts` | Caller-owned system text |
| `Plugins` | Contribution contracts and the in-process registry |

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
   the turn, or the user cancels.
7. After a completed turn, consider compaction from reported token usage.

Ctrl+C cancels the in-flight turn. Two Ctrl+C presses at an idle prompt exit.

## Plan and Work

These are product modes, not Crystal types.

- Plan registers read, glob, grep, todowrite, and question.
- Work registers those tools plus edit, write, and bash.

Switching modes replaces the first system message and the executor catalog.
The transcript is otherwise the same conversation.

## Approval

Every side-effect tool call is classified before invocation:

- Risk: Read, Write, Privileged, Forbidden.
- Authority: Workspace, OutsideWorkspace, Network, PrivilegedEscalation.
- Grant: Once, Session, Persistent.

Modes:

- Plan: read-only catalog. No edit, write, or bash.
- Default: Read auto-executes. Write and shell ask.
- AutoEdit: workspace file changes auto-execute. Shell still asks.
- Auto: workspace-bounded, policy-allowed actions auto-execute.
  Forbidden actions never auto-execute.

Persistent grants are stored in `~/.crystal/permissions.json`.

## Compaction

Crystal does not reduce context. When reported tokens cross the configured
fraction of the model window, the host:

1. Pins the current system text, workspace hints, recent turns, and open
   todos.
2. Replaces older tool noise with one Harness-authored summary message.
3. Falls back to dropping oldest tool results if summary generation fails.
   User messages are not dropped.

## Home directory

```text
~/.crystal/
  config.json
  credentials.json
  permissions.json
  sessions/<id>.json
  logs/
  plugins/
```

`credentials.json` is created with owner-only permissions. Environment
variables override file credentials. The `plugins` directory is reserved for
later assembly loading; the current product only registers in-process
contributions.

## Display

Spectre.Console supplies markup, color, and panels. The host owns input and
live layout. `AnsiConsole.Live` is not the session shell: it fights the line
editor.

The shell keeps a header (model, mode, approval, workspace), a scrollback
transcript, compact one-line tool rows, an approval card, and a footer
(context percent, tokens, tool count, elapsed time).

## Plugins

`IPlugin` contributes tools, chat-client factories, approval classifiers, or
slash commands through `PluginContribution`. Built-in tools and the DeepSeek
adapter register through the same table. Disk isolation with
`AssemblyLoadContext` is later work; do not pretend it exists.
