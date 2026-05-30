# Edict.ClaudeSkills

Installer that drops the [Edict](https://github.com/MalcolmMcNeely/Edict) consumer-facing skill bundle into `.claude/skills/`. Install as a `dotnet tool` and run `edict-skills install` from your repo root so Claude Code knows when to reach for which Edict-specific MCP tool while authoring code, contracts, silo wiring, tests, and diagnostics.

## Install

```
dotnet tool install --global Edict.ClaudeSkills --prerelease
```

## Run

From your repo root:

```
edict-skills install
```

The installer walks no further than the current working directory — it writes into `.claude/skills/<skill-name>/SKILL.md` relative to where you run it. For unusual layouts, pass `--target path/to/.claude/skills`.

Existing files are not overwritten; the installer reports which files it wrote and which it skipped.

## Skills

The bundle ships five consumer-facing skills, each scoped to "when working on a consumer app built on Edict" so they coexist with framework-development skills inside the Edict repo without auto-firing on framework code:

- `edict-authoring` — fires when adding a feature. Walks the Command Handler / Event Handler / Saga / Projection Builder / Table Projection Builder decision tree and triggers `edict_list_handlers` and `edict_list_route_keys`.
- `edict-contracts` — fires when defining or modifying Commands and Events. Covers `[EdictRouteKey]`, `[EdictStream]`, `[EdictTelemeterized]`, MessagePack-first, `[Alias]`, no `[Union]`; triggers `edict_lookup_adr` for the why.
- `edict-silo-wiring` — fires when editing `Program.cs`. Covers the `AddEdict*` matrix; triggers `edict_describe_silo_wiring`.
- `edict-testing` — fires when writing tests. Covers `EdictTestApp`, `WithConsumer`, `Replace`, `Send`, `Timeline`, `GetSagaProgress`, `GetProjectionRow`, `Drain`, `AdvanceClock`, and the chaos-default contract.
- `edict-diagnostics` — fires when investigating failures. Covers `IEdictDeadLetterRepository`, the W3C trace stitch, common failure shapes; triggers `edict_lookup_adr` for the relevant decisions.

## Version

Released in lockstep with the rest of Edict per ADR-0043. The version on this package matches every other `Edict.*` package in your graph.
