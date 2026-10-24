# MCP servers

Operators connect Crystal to Model Context Protocol servers. Each connected
server contributes model-callable tools. The host owns connections,
approval, output limits, timeouts, and shutdown. A server never bypasses
`ToolInvocationPolicy`.

MCP is separate from [external tools](external-tools.md). A tool set is code
the operator installs for every session. An MCP server is a process or
endpoint the session connects to and keeps open.

## What this build supports

- Tools from servers over stdio, streamable HTTP, or SSE.
- Resources, through two host tools.
- Prompts, as slash commands.
- Elicitation: a server can ask the operator for input.
- Roots: a server learns the workspace directory.
- A server's changed tool list, applied after the turn.
- Servers from `mcp.json` and `mcp/<name>/server.json`, in Home and in a
  workspace.
- The `/mcp` command, the `--mcp` launch option, and the `mcp` and
  `mcpToolApproval` settings.

Sampling and OAuth are not supported. The client does not declare sampling, so
a server cannot ask Crystal to call a model. A remote server that needs OAuth
fails to connect; use a static header instead. URL-mode elicitation, which
sends the operator to a browser, is declined.

## Where servers are defined

| Source  | Single file                  | One directory per server               |
| ------- | ---------------------------- | -------------------------------------- |
| Home    | `~/.crystal/mcp.json`        | `~/.crystal/mcp/<name>/server.json`    |
| Project | `<workspace>/.crystal/mcp.json` | `<workspace>/.crystal/mcp/<name>/server.json` |

A Project server of the same name replaces the Home server as a whole. Fields
are not merged. A Project entry that is invalid still replaces the Home entry,
so the server is not connected from either place.

Inside one source, a name that appears in both the file and a directory is
ambiguous. Neither is used and a note names both paths.

Project servers start under the same rule as project tool sets: when
`workspaceTrust` is on, the workspace is trusted before a session enters it.
With `workspaceTrust` off, project servers start without that check.

## `mcp.json`

```json
{
  "mcpServers": {
    "files": {
      "command": "npx",
      "args": ["-y", "@example/files-server", "."],
      "env": { "API_TOKEN": "${FILES_API_TOKEN}" }
    },
    "search": {
      "url": "https://example.test/mcp",
      "headers": { "Authorization": "Bearer ${SEARCH_API_KEY}" },
      "catalogs": ["plan", "work"],
      "toolTimeoutSeconds": 300
    }
  }
}
```

`server.json` holds one entry without the surrounding object. The directory
name is the server name.

| Field                | Meaning |
| -------------------- | ------- |
| `command`, `args`, `env` | Stdio server. The process starts in the workspace root. |
| `url`, `headers`     | HTTP server. |
| `type`               | `stdio`, `http`, or `sse`. Omitted: `command` means stdio, `url` means http. |
| `catalogs`           | `["work"]` by default. Add `"plan"` to offer the tools in Plan. |
| `toolTimeoutSeconds` | Seconds one call may run. Default 120. `"unlimited"` disables it. |
| `enabled`            | `true` by default. `false` keeps the server listed and never connects it. |

A definition needs exactly one of `command` or `url`. Unknown fields, repeated
fields, and a repeated server name skip that server and write a note. The file
is strict JSON: no comments and no trailing commas. A file larger than 256 KiB
is skipped.

Server names use letters, digits, `-`, and `_`, start with a letter, stay within
32 characters, and contain no `__`.

### Secrets

Write `${NAME}` in `command`, `args`, `env` values, `url`, and `headers` values
to read an environment variable of the Crystal process. That keeps secrets out
of project files that may be committed. A variable that is not set skips that
server, and the note names the variable but never a value. An empty value counts
as set.

## Tools

A server tool is offered to the model as `mcp__<server>__<tool>`. Characters
outside letters, digits, `-`, and `_` become `_`. A tool is skipped with a note
when its name is empty, too long, or already used by a built-in, plugin, or
external tool. The first server in name order keeps a contested name.

By default tools are in the Work catalog only. Add `"plan"` to `catalogs` to
offer them in Plan. Plan still rejects side effects through approval.

The host does not trust a server's description of itself. Descriptions are shown
to the model as given, so only connect servers you trust.

Results are returned as text. Image, audio, and binary content is not sent; a
short line names what was left out. A result larger than the tool output limit
is truncated. A server that reports an error produces a failed tool result. The
SDK may replace the server's error message with a generic one.

## Resources

When at least one connected server offers resources, two host tools appear in
the Work catalog. There is one pair for all servers, so the tool count does not
grow with the number of servers.

| Tool                  | Arguments      | Result |
| --------------------- | -------------- | ------ |
| `list_mcp_resources`  | `server`       | One line per resource: URI, name, type, description. At most 200 lines. |
| `read_mcp_resource`   | `server`, `uri`| The text of the resource. Binary content is named, not sent. |

`server` must be a connected server that offers resources; the error names the
valid ones. Both names are reserved, so a server tool cannot take them.

Plan has the pair only when some server lists `"plan"` in `catalogs`. Calls use
the same approval and timeout as that server's tools.

## Prompts

A server prompt becomes the slash command `/mcp__<server>__<prompt>`, listed in
the command menu with the server's description. The command asks the server for
the prompt and submits its text as an ordinary user message, so the turn,
approval, and history behave as if the operator had typed it.

Arguments are `name=value` pairs. Quote a value that has spaces:

```text
/mcp__docs__review file=src/app.cs style="short and plain"
```

A missing required argument, an unknown argument, a repeated argument, or an
unclosed quote is reported and no turn starts. A prompt with several messages
is joined into one user message; an assistant message is prefixed with
`Assistant:`. Image, audio, and binary parts are named but not sent.

A prompt command name that is already taken is skipped with a note. Prompt
commands are not available while a turn is running.

## Elicitation

A server can ask the operator for input while a tool runs. Crystal shows it with
the same question panel as the `question` tool, headed `MCP <server>`, one
question per field, and returns the answers to the server.

Supported fields are text, number, integer, yes or no, and a single choice from
a fixed list. A request with more than 8 fields, a multiple-choice or other
unsupported field, or URL mode is declined without asking. An empty answer to an
optional field leaves it out. An answer that does not fit its field, such as a
word for a number, declines the request. Rejecting the panel cancels it.

`crystal run` has no operator, so every elicitation there is cancelled.

## Roots

Crystal declares the roots capability. A server that asks receives one root:
the current workspace directory as a `file://` URI. It follows `/cd` and
`/space`, because those reconnect the servers. Session directory access from
`/access` is not shared. Roots are informational; a server is not confined to
them.

## Changed tool lists

When a server announces a changed tool list, Crystal does not change the tools
during a turn. It reconnects the configured servers after the turn ends, writes
`MCP tools changed  <server>`, and the next turn sees the new tools. Prompt
commands refresh at the same time.

## Approval

Every call is at least a workspace write and is reviewed under the current
approval mode. A server's `readOnlyHint` is a claim by that server.

`mcpToolApproval` decides whether the claim counts, separately for Home and
Project servers:

- `host` (default): every call is a workspace write.
- `author`: a tool the server marks read-only is treated as a read.

Servers a front end passes in for one session follow the Project member.
Session and persistent grants work as for other tools.

## Timeouts and failures

- Connection: 30 seconds in total. Servers that do not support the newest
  protocol revision are probed for 0.5 seconds before the older handshake runs.
- Listing tools and prompts: 30 seconds.
- A call, a resource read, or a prompt request: `toolTimeoutSeconds`.
  Cancelling the turn cancels the call.

A server that fails to start, connect, or list its tools is reported as a note
and the session continues with the other servers. A failed stdio start includes
the last few lines the process wrote to stderr, cleaned of control characters.
Connections are opened in parallel, so a hung server delays startup by at most
the connection timeout.

## Lifecycle

- Servers connect when the session starts, after external tools load.
- `/cd`, `/space`, and resuming into another workspace close every connection
  and connect the servers of the new workspace.
- `/clear` keeps connections.
- Closing the session closes every connection and stops stdio processes.

## Commands

In a session:

| Command                                  | Effect |
| ---------------------------------------- | ------ |
| `/mcp`                                   | Connected servers with source, transport, state, and tool count. |
| `/mcp list`                              | Every configured server, including disabled and invalid ones. |
| `/mcp show <server> [home\|project]`     | One server's definition summary and live state. |
| `/mcp enable\|disable <server> [home\|project]` | Set `enabled` in the file, then reconnect. |
| `/mcp on`, `/mcp off`                    | Turn MCP on or off, save `mcp`, and reconnect. |
| `/mcp reload`                            | Read the configuration again and reconnect. |
| `/mcp approval`                          | Show the approval choice for Home and Project. |
| `/mcp home\|project [author\|host]`      | Show or set the approval choice for one source. |

From a shell, without a session:

```text
crystal mcp list
crystal mcp show <server>
crystal mcp enable <server>
crystal mcp disable <server>
```

They take `--home`, `-w|--workspace`, `--source home|project`, and
`--format text`, the same as `crystal tools`. They change files only; they do
not connect to anything.

When a name exists in both Home and Project, the Project server is the one
that takes effect, so it is the default target. Pass `home` or `--source home`
to change the replaced Home server instead.

`enable` and `disable` rewrite only that server's `enabled` field. In a shared
`mcp.json` every other server and every unknown field is kept, but the file is
written again as indented JSON, so its original spacing is not preserved. An
invalid server cannot be enabled or disabled; fix its file first.

None of these print a server's command, arguments, environment, headers, or
URL, because they can hold secrets. The in-session commands need an idle
session.

`/status` shows the MCP switch under Options. `/status full` also shows the
number of connected servers and MCP tools.

`--mcp on|off` overrides `mcp` for one process, for both `crystal` and
`crystal run`. It is not written to `config.json`.

## Settings

```json
{
  "mcp": false,
  "mcpToolApproval": { "home": "author" }
}
```

`mcp` defaults to `true` and is written only when `false`. `mcpToolApproval`
members default to `host` and are written only when changed.
