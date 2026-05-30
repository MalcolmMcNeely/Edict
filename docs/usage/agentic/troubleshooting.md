# Troubleshooting

Two failure shapes worth a page of their own: an MCP response that no longer matches the framework you're actually coding against, and the question of how much of this story works when the editor isn't Claude Code.

## Version drift

A consumer asks: "Why is `edict_list_handlers` returning a type I deleted last week?"

The first move is `edict_describe_mcp_state`. The response carries the loaded solution path, the indexed-handler count, and the full Edict version report — the `edict-mcp` tool version alongside every `Edict.*` library version referenced by the loaded solution. The drift status reads `clean` when they line up, and one of three other values when they don't.

`edict-mcp` always warns, never blocks. Even severe drift returns warnings, not errors, so the agent can still call every tool — the consumer decides whether to act on what the report says.

### `drifted` — `dotnet tool update Edict.Mcp --prerelease`

The tool version differs from at least one `Edict.*` library version the solution references. The embedded ADRs, the `AddEdict*` catalogue used by `edict_describe_silo_wiring`, and the role enum used by `edict_list_handlers` may describe a different framework version than the code you're editing. Run `dotnet tool update Edict.Mcp --prerelease` to realign the tool with the libraries — or align the `Edict.*` `PackageReference` versions to the tool, whichever direction matches your intent.

### `no-edict-references` — wrong workspace

The loaded solution contains zero `Edict.*` references. The usual cause is that `edict-mcp` is pointed at the wrong solution — a sibling solution under the same root, or the IDE's working directory drifted to a folder that isn't the repo root. Check `loadedSolutionPath` in the same `edict_describe_mcp_state` response. If it's wrong, add `--solution path/to.slnx` to the `args` array of the `edict` entry in `.mcp.json` and restart the MCP server.

### `inconsistent-library-versions` — align `PackageReference` versions

Two or more distinct `Edict.*` versions appear across the solution's projects — a lockstep violation. The MCP responses are still grounded in the consumer's solution, but the consumer's solution itself is internally inconsistent. Align every `Edict.*` `PackageReference` to the same version (a `Directory.Packages.props` central-package-management file is the easiest tool for this) before relying on what the agent suggests.

### Where the warning surfaces

Drift is visible without restarting Claude Code:

- **Stderr at MCP server startup** — Claude Code surfaces this in its server-logs panel. The block names the tool version, the distinct library versions found, the drift class, and the remediation command. Suppressed when the workspace is `clean`.
- **`edict_describe_mcp_state`** — the full report inline as the `edictVersionReport` field.
- **`edict_list_handlers`, `edict_list_route_keys`, `edict_describe_silo_wiring`** — each response carries a top-level `driftStatus` field, so the agent sees the warning on every Roslyn-walk call without paying for the full report.

## Standalone use for non-Claude editors

A consumer asks: "I'm on Cursor — does any of this work for me?"

Yes. Two pieces are doing two different jobs, and only one of them is Claude-Code-specific.

`Edict.Mcp` is a Model Context Protocol server. MCP is an open protocol; any editor with MCP support — Cursor, Zed, others — wires it the same way Claude Code does. Add the `edict` entry to whatever the editor uses to register MCP servers, in the same shape `.mcp.json` carries, and the six tools become callable. Drift warnings, the `driftStatus` field, `edict_describe_mcp_state` — all of it travels.

The skill bundle is plain markdown. `dotnet edict-skills install` writes one file per skill under `.claude/skills/<skill-name>/SKILL.md` from the directory you run it in — five files: `edict-authoring`, `edict-contracts`, `edict-silo-wiring`, `edict-testing`, `edict-diagnostics`. Each is a self-contained markdown document with a YAML frontmatter `description` field. Any agent can read those files as context.

What's Claude-Code-specific is the skill-firing mechanism: Claude Code reads each skill's `description`, matches it against what the user just asked, and loads the body automatically. That matcher does not exist in other editors. In Cursor or any other agent, you surface the relevant body manually — `@`-mention `.claude/skills/edict-authoring/SKILL.md` in a chat session before asking for a new handler, or paste the body into a project-level rules file. The prescriptions ("call `edict_list_handlers` before writing code", "boot `EdictTestApp` instead of mocking Orleans") apply just the same; the agent reads them as instructions, the MCP tools answer the calls.

## See also

- Agentic tooling — [Setup](setup.md), [Skills](skills.md), [MCP tools](mcp-tools.md).
- ADRs — [0044 — Agentic tooling](../../adr/0044-agentic-tooling.md).
