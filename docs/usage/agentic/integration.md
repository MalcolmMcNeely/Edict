# Integrating with your agentic workflow

The Edict skill bundle and MCP server are designed to plug into whatever planning, design, and implementation loop you already use with your AI agent. They do not assume — or require — a particular workflow shape.

This page explains what activates automatically, where the gap is, and how to close that gap when it matters. The setup the Edict repo uses for its own development is the worked example throughout; it is not a prescription.

## What activates by itself

The five skills in `Edict.ClaudeSkills` are scoped by trigger clauses on consumer-app work — adding a feature, defining a contract, editing `Program.cs`, writing tests, investigating a failure. When the agent recognises one of those shapes mid-conversation, the skill body loads and the MCP tools it names (`edict_list_handlers`, `edict_lookup_adr`, etc.) become the next move.

This happens during the **implementation phase** of any workflow. The trigger is the task shape, not the workflow stage — so it does not matter whether the agent reached "implement this Command" via TDD, via a checklist from an issue tracker, or via a free-form "add this for me" prompt. The bundle activates the same way.

No wiring into your other skills is required for this path.

## Where the gap is

Most agentic workflows have phases that run **before** code gets touched: design grilling, PRD drafting, issue breakdown, architecture review. The skills that drive those phases — whether they ship with your agent, live in your dotfiles, or are bespoke to your team — are typically project-agnostic. They do not know that `edict_lookup_adr` exists, or that `edict_list_handlers` would tell them whether a proposed handler already exists.

The result: an agent grilling you on a proposed Edict feature will ask good general design questions but will not ground them in the actual handler inventory, route-key map, or ADR record. It will write a PRD without naming the existing handlers it integrates with. It will break a feature into issues without checking which `AddEdict*` extensions are already wired.

Whether this matters depends on how much your planning loop benefits from concrete project state. For a small consumer app the gap is small; for one with many handlers, several sagas, and a long ADR record, the gap is real.

## The example — what the Edict project itself does

The Edict repo uses a four-skill planning-to-implementation loop, all generic:

1. **grill-with-docs** — stress-tests a design against the project's CONTEXT.md and ADRs.
2. **to-prd** — converts the aligned design into a PRD on the issue tracker.
3. **to-issues** — breaks the PRD into tracer-bullet vertical-slice issues.
4. **tdd** — implements one slice at a time, red-green-refactor.

The implementation phase (tdd) **does** activate the bundle. When the loop reaches an Edict-consumer file edit, `edict-authoring` / `edict-contracts` / `edict-silo-wiring` / `edict-testing` fire on the relevant edit shapes and pull in their MCP tools. The bundle pays off here without any wrapper.

The planning phase (grill-with-docs, to-prd, to-issues) **does not** activate the bundle. Those skills are generic and do not name any `edict_*` tool. A consumer running `grill-with-docs` on an Edict app gets useful generic grilling against CONTEXT.md and ADRs read directly from disk, but never sees the handler inventory or the live route-key map.

This is the gap, made concrete. Your own workflow probably has equivalents.

## Closing the gap when it matters

If a planning-phase skill in your workflow would benefit from MCP grounding, the cleanest seam is a **thin wrapper skill** that names the relevant MCP tools by name in its body. The wrapper sits alongside (or replaces) the generic skill for Edict-flavoured work.

A worked example — an Edict-aware grilling skill. Its body would reference the generic grilling skill's structure but add concrete tool-call prescriptions:

> Before stress-testing a proposed handler, call `edict_list_handlers` to see whether a handler for the bound Command already exists, and `edict_list_route_keys` to see whether the route key is taken. Ground your grilling questions in the returned inventory. For wire-format or substrate trade-offs in the proposed design, call `edict_lookup_adr` with the relevant topic; quote the ADR's conclusion in the grilling question.

Three things to keep in mind when authoring a wrapper:

- **Name the tools explicitly.** A reference like "use the Edict MCP" will not make the agent call the right tool reliably. Use the literal tool name (`edict_lookup_adr`, not "the ADR tool").
- **Keep the wrapper to the gap.** If the generic skill already handles the substance, a one-paragraph wrapper that adds the tool prescriptions is enough. Do not re-state the generic skill.
- **Drift-guard the pairings.** If you ship more than one wrapper, write a test that asserts every `edict_*` token in your skill bodies resolves to a registered tool, and every tool the wrappers reference still exists. The Edict project does this in `Edict.AgenticTooling.Architecture.Tests`; mirror the shape for your own bundle.

## When not to wrap

A wrapper that is not pulling its weight is friction. Two heuristics for when to leave the generic skill alone:

- **The implementation-phase skills already cover the substance.** TDD is the clearest case: `edict-authoring` and `edict-testing` already prescribe inventory checks and the `EdictTestApp` test shape. An `edict-tdd` wrapper would mostly duplicate them. The Edict project does not ship one and does not recommend you write one until you have seen the composition fail in practice.
- **Your planning loop is light-weight.** If your design step is one paragraph in a PR description rather than a multi-turn grilling session, the gain from grounded MCP tools at planning time is small.

## See also

- [Setup](setup.md) — install the bundle and the MCP server.
- [Skills](skills.md) — what each of the five skills does.
- [MCP tools](mcp-tools.md) — per-tool reference for the six MCP tools.
- [Troubleshooting](troubleshooting.md) — version drift and workspace mismatches.
- ADRs — [0044 — Agentic tooling](../../adr/0044-agentic-tooling.md).
