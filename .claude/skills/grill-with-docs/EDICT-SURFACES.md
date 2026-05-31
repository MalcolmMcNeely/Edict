# EDICT-SURFACES

A grill on Edict-internal work captures requirements for **every surface the change touches**, not only the code. This file lists those surfaces and the question the grill asks for each.

Walk these after the language and decisions have stabilised, one question at a time — same interview shape as the rest of the skill. Capture the answer to each as a design requirement before the grill closes.

The axes are not a checklist to mechanically tick. Most changes touch a few; a few touch most. The point is the *ask*, not exhaustive paperwork. A four-answer reply of "no, no, no, yes" is the right shape for most grills.

## Axes

### 1. Domain vocabulary — `CONTEXT.md`

Does this change introduce a new domain concept, rename an existing one, or modify an `_Avoid_` list?

If yes, draft the term inline as part of the grill — a one-sentence definition plus `_Avoid_` list — using the format in [CONTEXT-FORMAT.md](./CONTEXT-FORMAT.md). CONTEXT.md is the agentic-tooling ground truth (`edict_describe_glossary_term` reads it); stale glossary entries are silently consumer-facing.

### 2. ADR-worthiness — `docs/adr/`

Apply the three-criteria gate in [ADR-FORMAT.md](./ADR-FORMAT.md). Hard to reverse? Surprising without context? Real trade-off?

If all three are true, the design requirement is the ADR. Capture its title and the 1-3 sentence shape before the grill closes. The ADR is also surfaced through `edict_lookup_adr` — a missing ADR is a missing answer to a consumer's "why."

### 3. Skills bundle — `Edict/Edict.ClaudeSkills/Skills/*.md`

Does the change alter what a consumer should do when authoring contracts, wiring the silo, testing, or diagnosing in the affected area?

The five existing skills cover the five edit shapes:

- `edict-authoring` — adding a Command, Event, or any handler/saga/projection class
- `edict-contracts` — defining or modifying `EdictCommand` / `EdictEvent` subclasses
- `edict-silo-wiring` — editing `Program.cs` or any silo-wiring file
- `edict-testing` — writing tests against an Edict consumer app
- `edict-diagnostics` — investigating a runtime failure

If the change fits an existing skill, name which body edits. If it does not fit any of the five, the change is either internal-only (no skill update needed) or it warrants a new skill — capture which.

### 4. MCP tools — `Edict/Edict.Mcp/Tools/*.cs`, registered in `McpToolRegistry.cs`

Does the change introduce behaviour an agent needs to introspect, or change the contract of a tool that already exists?

The six registered tools are `edict_describe_mcp_state`, `edict_describe_glossary_term`, `edict_lookup_adr`, `edict_list_handlers`, `edict_list_route_keys`, `edict_describe_silo_wiring`.

A skill body recommending a tool that does not exist is the failure mode the interlock test in `Edict.AgenticTooling.Architecture.Tests` catches. Capture the tool requirement here rather than discover it from a red test later.

### 5. Consumer-facing docs — `docs/usage/*`

Which page covers the change for a consumer? Possible homes:

- `docs/usage/concepts/*` — concept pages, one per first-class concept
- `docs/usage/wiring/*` — per-substrate wiring (azure-streaming, azure-persistence, postgres, kafka)
- `docs/usage/agentic/*` — skills, MCP, integration, troubleshooting, setup
- `docs/usage/testing/*` — setup, seams, probes, chaos
- `docs/usage/getting-started.md`
- `docs/operations/*` — alerts, observability

If no page exists and the change introduces a first-class concept, a new page is a deliverable. If a page exists and the prose goes stale, name the edit.

### 6. Cross-package drift guards — `Edict.AgenticTooling.Architecture.Tests`

If the change introduces a new trigger clause, tool name, or glossary anchor that the skills or MCP tools reference, is the existing interlock test the right shape to assert no orphan?

If a new assertion is needed (new tool ↔ skill body pairing, new glossary term ↔ skill reference), capture it as a deliverable. Drift caught at design time costs less than drift caught at test time.

### 7. Sample app — `Sample/*`

Does the change need a demo handler/saga/projection to exist in the Sample app, or extend one that already exists?

The Sample app is the sales-demo framing for strangers cloning the repo — the bar is "does a visitor see the new concept *used*?" If yes, that's a deliverable; capture which Sample project (`Sample.Web`, `Sample.Silo`, contract assemblies) needs the change.

### 8. Wire shape — `Edict.Contracts` and persisted state

Does the change touch a wire-format type — anything MessagePack-serialised on a stream hop, persisted as grain state, or carried on `EdictCommandResult`?

Edict is pre-release with no released consumers, so the answer is usually "no compat constraint." The grill still asks because the Verify contract round-trip is the wire drift guard (ADR 0007) — a wire change needs the snapshot regenerated, and that's a design requirement worth recording so it does not surprise the implementation slice.

## Rules

- **Ask one axis at a time.** Do not present the list. Walk it as part of the interview.
- **Capture the answer as a requirement.** Not a TODO, not a follow-up — a design requirement that travels with the PRD or issue when the grill closes.
- **Skip axes the change does not touch.** The point is the ask, not exhaustive paperwork.
- **Vocabulary anchors are pointers, not copies.** When an axis references an existing skill body, MCP tool, or doc page, the authoritative content lives there. Do not duplicate it here, and refresh this file's anchor list when the surface itself moves.
