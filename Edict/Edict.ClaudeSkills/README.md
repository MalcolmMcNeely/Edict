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

This release is a tracer-bullet. One placeholder skill body is bundled so the install plumbing has something concrete to land. The full five-skill bundle — `edict-authoring`, `edict-contracts`, `edict-silo-wiring`, `edict-testing`, `edict-diagnostics` — ships in later prereleases.

## Version

Released in lockstep with the rest of Edict per ADR-0043. The version on this package matches every other `Edict.*` package in your graph.
