# Agentic-tooling setup

Two `dotnet tool`s install together: `Edict.Mcp` (the MCP server the agent queries for handlers, route keys, silo wiring, glossary terms, and ADRs) and `Edict.ClaudeSkills` (the five skills that tell the agent which tool to reach for, and when).

## Install

From your repo root:

```
dotnet new tool-manifest
dotnet tool install Edict.Mcp --prerelease
dotnet tool install Edict.ClaudeSkills --prerelease
dotnet tool restore
dotnet edict-skills install
```

Check `.config/dotnet-tools.json` into source control. Every developer on the repo gets the same MCP server and the same skill bundle on `dotnet tool restore`, locked to your `Edict.*` library version.

## What lands in your repo

`dotnet edict-skills install` writes two things into the directory you run it from:

- **`.claude/skills/<skill-name>/SKILL.md`** — one folder per skill. Existing skill files are not overwritten; the installer reports each file it wrote and each it skipped.
- **`.mcp.json`** — created only if absent, with `edict-mcp` registered as a server in the form matching the install mode (manifest or global). If the file already exists it is left untouched and the installer prints the entry you should add. Hand-authored comments, ordering, and other server entries stay exactly as you wrote them.

Re-running the installer is a no-op once both are in place.

## Global install

Single-dev workflow:

```
dotnet tool install --global Edict.Mcp --prerelease
dotnet tool install --global Edict.ClaudeSkills --prerelease
edict-skills install
```

The manifest path is the team default because it version-pins the MCP server's embedded ADRs and the installed skill bodies to your repo's `Edict.*` library version. A global install can drift behind the libraries you code against; [troubleshooting.md](troubleshooting.md) covers how the MCP server surfaces the drift.

## See also

- Agentic tooling — [Skills](skills.md), [MCP tools](mcp-tools.md), [Troubleshooting](troubleshooting.md).
- Per-package install reference — [`Edict.Mcp`](../../../Edict/Edict.Mcp/README.md), [`Edict.ClaudeSkills`](../../../Edict/Edict.ClaudeSkills/README.md).
- ADRs — [0044 — Agentic tooling](../../adr/0044-agentic-tooling.md).
