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

The installer writes into `.claude/skills/<skill-name>/SKILL.md` relative to where you run it. For unusual layouts, pass `--target path/to/.claude/skills`.

Existing files are not overwritten; the installer reports which files it wrote and which it skipped.

## Skills

Five skills land, each scoped to "when working on a consumer app built on Edict":

- `edict-authoring` — fires when adding a feature. Walks the Command Handler / Event Handler / Saga / Projection Builder / Table Projection Builder decision tree.
- `edict-contracts` — fires when defining or modifying Commands and Events. Covers `[EdictRouteKey]`, `[EdictStream]`, `[EdictTelemeterized]`, MessagePack-first.
- `edict-silo-wiring` — fires when editing `Program.cs`. Covers the `AddEdict*` matrix.
- `edict-testing` — fires when writing tests. Covers `EdictTestApp`, the probes, `Replace`, and chaos-default.
- `edict-diagnostics` — fires when investigating failures. Covers `IEdictDeadLetterRepository`, the trace stitch, and common failure shapes.

## Learn more

See [docs/usage/getting-started.md](https://github.com/MalcolmMcNeely/Edict/blob/main/docs/usage/getting-started.md) for the smallest valid sample.
