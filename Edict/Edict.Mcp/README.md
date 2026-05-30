# Edict.Mcp

Model Context Protocol server for the [Edict](https://github.com/MalcolmMcNeely/Edict) CQRS framework. Install as a `dotnet tool` and wire it into `.mcp.json` so an AI agent (Claude Code, Cursor, etc.) can query an Edict-based solution's handlers, route keys, silo wiring, glossary, and decision records while it writes code.

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

- `edict_describe_glossary_term` — fuzzy lookup of Edict's domain language. Case-insensitive, the optional `Edict` prefix elidable (`Saga`, `saga`, `EdictSaga` all resolve).
- `edict_lookup_adr` — fuzzy retrieval of an Edict decision record by number or title substring.
- `edict_list_handlers` — every consumer-defined `EdictCommandHandler` / `EdictEventHandler` / `EdictSaga` / `EdictProjectionBuilder` / `EdictTableProjectionBuilder` in the solution, with bound contract type, route-key property, declaring assembly, and source location.
- `edict_list_route_keys` — derived view over the handler inventory surfacing route-key collisions and shares.
- `edict_describe_silo_wiring` — locates `Program.cs`, walks the `ISiloBuilder` chain, reports the wired `AddEdict*` extensions plus the known-but-missing ones.
- `edict_describe_mcp_state` — self-diagnostic. Reports the loaded solution path, indexed-handler count, and registered tool list.

## Learn more

See [docs/usage/getting-started.md](https://github.com/MalcolmMcNeely/Edict/blob/main/docs/usage/getting-started.md) for the smallest valid sample.
