# Edict.ClaudeSkills

Installer that drops the [Edict](https://github.com/MalcolmMcNeely/Edict) consumer-facing skill bundle into `.claude/skills/` and wires `Edict.Mcp` into `.mcp.json`. Install as a `dotnet tool` and run `edict-skills install` from your repo root so Claude Code knows when to reach for which Edict-specific MCP tool while authoring code, contracts, silo wiring, tests, and diagnostics.

Requires the .NET 10 SDK on the developer machine.

## Install (recommended)

Pin the tool version per repo via a local manifest. This keeps the installed skill bodies aligned with the consumer's `Edict.*` library version by construction.

```
dotnet new tool-manifest
dotnet tool install Edict.ClaudeSkills --prerelease
dotnet tool restore
```

Then from your repo root:

```
dotnet edict-skills install
```

Check the manifest into source control. Every developer on the repo gets the same skill bundle on `dotnet tool restore`.

## Install (global)

If you prefer a machine-wide install:

```
dotnet tool install --global Edict.ClaudeSkills --prerelease
edict-skills install
```

A global install will not version-pin the installed skill bodies to your `Edict.*` library version.

## Where it writes

The installer writes `.claude/skills/<skill-name>/SKILL.md` relative to where you run it. For unusual layouts, pass `--target path/to/.claude/skills`.

Existing skill files are not overwritten. The installer reports which files it wrote and which it skipped.

## `.mcp.json` wiring

After installing skills, the installer inspects `.mcp.json` in the current directory and reports what it did. It never modifies an existing `.mcp.json` — only creates one when absent.

The behaviour matrix:

- **File absent** — creates `.mcp.json` with the `edict` entry in the form matching the detected install mode (manifest or global). This is the only case in which the installer writes the file.
- **File present, no `edict` entry under `mcpServers`** — prints the entry you should add. Your file is left untouched so any comments, ordering, or other servers stay exactly as you authored them.
- **File present, `edict` entry already in the matching form** — reports the file as already wired and does nothing.
- **File present, `edict` entry in the other form** — you have a manifest install but the entry is the global form, or vice versa. Prints the replacement entry; your file is left untouched.

Detection: an entry whose `command` is `dotnet` is classified as the manifest form; anything else is the global form. The installer reads `.mcp.json` permissively (comments and trailing commas tolerated) so a hand-curated file is not rejected for cosmetic reasons.

## Skills

Five skills land, each scoped to "when working on a consumer app built on Edict":

- `edict-authoring` — fires when adding a feature. Walks the Command Handler / Event Handler / Saga / Projection Builder / Table Projection Builder decision tree.
- `edict-contracts` — fires when defining or modifying Commands and Events. Covers `[EdictRouteKey]`, `[EdictStream]`, `[EdictTelemeterized]`, MessagePack-first.
- `edict-silo-wiring` — fires when editing `Program.cs`. Covers the `AddEdict*` matrix.
- `edict-testing` — fires when writing tests. Covers `EdictTestApp`, the probes, `Replace`, and chaos-default.
- `edict-diagnostics` — fires when investigating failures. Covers `IEdictDeadLetterRepository`, the trace stitch, and common failure shapes.

## Standalone use

The skill bundle is plain markdown. It works without `Edict.Mcp` wired up — Claude Code reads the bodies as context regardless. Cursor and other non-Claude editors that support skill-shaped markdown can consume the files directly.

## Learn more

See [docs/usage/getting-started.md](https://github.com/MalcolmMcNeely/Edict/blob/main/docs/usage/getting-started.md) for the smallest valid sample.
