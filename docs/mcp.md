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
- Servers from `mcp.json` and `mcp/<name>/server.json`, in Home and in a
  workspace.
- The `/mcp` command, the `--mcp` launch option, and the `mcp` and
  `mcpToolApproval` settings.

Resources, prompts, elicitation, roots, sampling, and OAuth are not supported.
A server that offers them still connects and its tools work. The client does
not declare those capabilities, so a server cannot use them.

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
- Listing tools: 30 seconds.
- A call: `toolTimeoutSeconds`. Cancelling the turn cancels the call.

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

| Command        | Effect |
| -------------- | ------ |
| `/mcp`         | List connected servers with source, transport, state, and tool count. |
| `/mcp on`      | Turn MCP on, save `mcp`, and reconnect. |
| `/mcp off`     | Turn MCP off, save `mcp`, and disconnect. |
| `/mcp reload`  | Read the configuration again and reconnect. |

The status never shows arguments, environment, headers, or URLs, because they
can hold secrets. These commands need an idle session.

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
