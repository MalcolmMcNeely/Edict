# Edict.Mcp

Model Context Protocol server for the [Edict](https://github.com/MalcolmMcNeely/Edict) CQRS framework. Install as a `dotnet tool` and wire it into `.mcp.json` so an AI agent (Claude Code, Cursor, etc.) can query an Edict-based solution's handlers, route keys, silo wiring, glossary, and decision records while it writes code.

Requires the .NET 10 SDK on the developer machine.

## Install (recommended)

Pin the tool version per repo via a local manifest. This keeps the embedded ADRs, the `AddEdict*` catalogue, and the version drift check aligned with the consumer's `Edict.*` library version by construction.

```
dotnet new tool-manifest
dotnet tool install Edict.Mcp --prerelease
dotnet tool restore
```

Wire it into `.mcp.json` at the repo root:

```json
{
  "mcpServers": {
    "edict": { "command": "dotnet", "args": ["edict-mcp"] }
  }
}
```

Check both files into source control. Every developer on the repo gets the same MCP wiring on `dotnet tool restore`.

## Install (global)

If you prefer a machine-wide install:

```
dotnet tool install --global Edict.Mcp --prerelease
```

```json
{
  "mcpServers": {
    "edict": { "command": "edict-mcp" }
  }
}
```

A global install will not version-pin the embedded docs and catalogue to your `Edict.*` library version. The drift check below will flag this.

## Solution discovery

For a repo with multiple solutions, pass `--solution path/to.slnx` in the `.mcp.json` args. Otherwise the server walks up from the current working directory looking for a `.slnx` / `.sln`.

## Tools

- `edict_describe_glossary_term` — fuzzy lookup of Edict's domain language. Case-insensitive, the optional `Edict` prefix elidable (`Saga`, `saga`, `EdictSaga` all resolve).
- `edict_lookup_adr` — fuzzy retrieval of an Edict decision record by number or title substring.
- `edict_list_handlers` — every consumer-defined `EdictCommandHandler` / `EdictEventHandler` / `EdictSaga` / `EdictProjectionBuilder` / `EdictTableProjectionBuilder` in the solution, with bound contract type, route-key property, declaring assembly, and source location.
- `edict_list_route_keys` — derived view over the handler inventory surfacing route-key collisions and shares.
- `edict_describe_silo_wiring` — locates `Program.cs`, walks the `ISiloBuilder` chain, reports the wired `AddEdict*` extensions plus the known-but-missing ones.
- `edict_describe_mcp_state` — self-diagnostic. Reports the loaded solution path, indexed-handler count, the full version drift report, and the registered tool list. Run this first when results look off.

## Version drift warnings

At startup the server compares its own tool version against the `Edict.*` library versions referenced by the loaded solution. The check classifies the workspace as one of:

- **drifted** — the tool version differs from at least one referenced library version. The embedded ADRs and `AddEdict*` catalogue may describe a different surface than the libraries you're coding against. Remediate with `dotnet tool update Edict.Mcp --prerelease` (or align the `PackageReference` versions).
- **no-edict-references** — the loaded solution contains no `Edict.*` references. The tool is running, but it has no library to check against. Confirm `edict-mcp` is pointed at the right solution.
- **inconsistent-library-versions** — two or more distinct `Edict.*` library versions appear across the solution's projects. Align all `Edict.*` `PackageReference` versions before relying on the tool's responses.

The warning surfaces in three places:

- **Stderr at server startup** — formatted block written once when the server boots, suppressed when the workspace is clean.
- **`edict_describe_mcp_state`** — full report (tool version, every reference, the three classification booleans, derived status) inline as the `edictVersionReport` field.
- **`edict_list_handlers`, `edict_list_route_keys`, `edict_describe_silo_wiring`** — each response carries a top-level `driftStatus` string (`clean`, `drifted`, `no-edict-references`, or `inconsistent-library-versions`) so the agent sees the warning on every Roslyn-walk call without paying for the full report.

## Scope

The six tools cover Tier 1 — static observation of a consumer's solution. Tier 2 (live observation: dead-letter queries, saga progress, projection rows, outbox metrics snapshots) is out of scope for now and will land when consumer demand surfaces.

## Learn more

See [docs/usage/getting-started.md](https://github.com/MalcolmMcNeely/Edict/blob/main/docs/usage/getting-started.md) for the smallest valid sample.
