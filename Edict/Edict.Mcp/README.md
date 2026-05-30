# Edict.Mcp

Model Context Protocol server for the [Edict](https://github.com/MalcolmMcNeely/Edict) CQRS framework. Install as a `dotnet tool` and wire it into `.mcp.json` so an AI agent (Claude Code, Cursor, etc.) can query an Edict-based solution's handlers, route keys, silo wiring, glossary, and ADRs while it writes code.

## Install

```
dotnet tool install --global Edict.Mcp --prerelease
```

## Wire it into `.mcp.json`

```json
{
  "mcpServers": {
    "edict": {
      "command": "edict-mcp"
    }
  }
}
```

For a repo with multiple solutions, pass `--solution path/to.slnx`. Otherwise the server walks up from the current working directory looking for a `.slnx` / `.sln`.

## Tools

This release is a tracer-bullet. The only tool advertised today is `edict_describe_mcp_state` — a self-diagnostic that reports the loaded solution path, indexed-handler count, and registered tool list. Use it to verify the MCP server found your workspace. The full tool surface (`edict_describe_glossary_term`, `edict_lookup_adr`, `edict_list_handlers`, `edict_list_route_keys`, `edict_describe_silo_wiring`) ships in later prereleases.

## Version

Released in lockstep with the rest of Edict per ADR-0043. The version on this package matches every other `Edict.*` package in your graph.
