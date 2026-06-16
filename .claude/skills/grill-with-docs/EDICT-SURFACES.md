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

### 7. Wire shape — `Edict.Contracts` and persisted state

Does the change touch a wire-format type — anything MessagePack-serialised on a stream hop, persisted as grain state, or carried on `EdictCommandResult`?

Edict is pre-release with no released consumers, so the answer is usually "no compat constraint." The grill still asks because the Verify contract round-trip is the wire drift guard (ADR 0007) — a wire change needs the snapshot regenerated, and that's a design requirement worth recording so it does not surprise the implementation slice.

Decide breaking-ness here too, because it is a consumer-surface judgment the committing agent is badly placed to make from a green diff. A change is **breaking** when it removes, renames, or alters the signature of a public `Edict*` member — concretely, when the public-surface allow-list in `Edict.Architecture.Tests` would change by anything other than a pure addition. This is wider than wire shape: a public-API rename with no wire change (e.g. renaming a projection taxonomy) is breaking. Adding-only is not. When the change is breaking, capture that as a design requirement so it travels with the PRD and the issue (a `Breaking:` line), and the implementation commit marks it `feat!:`/`fix!:` with a `BREAKING CHANGE:` footer. The bump itself stays a manual release-time choice (ADR-0059) — the marker only routes the change into the release-notes Breaking section, it does not derive the version.

### 8. Consumer test seams — `Edict.Testing`

`Edict.Testing` is a consumer-facing production surface — it ships as a package and consumers reference it to test their apps (`EdictTestApp`, the `FakeTimeProvider` virtual clock, `AdvanceClock`, `Replace<TService>`, probes, `Drain()`). It is **not** internal framework test infra (internal tests must never depend on it). This axis is distinct from axis 3's `edict-testing` skill, which documents *how* to use the seams, and from axis 5's `docs/usage/testing/*`, which is prose: here the question is whether a new seam must be **built**.

Does the change introduce a primitive a consumer needs to drive or observe deterministically in a test? Anything that fires on a clock or a background trigger — a new timer, lifecycle hook, timeout, or outcome — needs an interval-agnostic, virtual-clock-driven seam so the consumer can assert without wall-clock waits, and so chained scenarios (act → fire → assert → fire → assert) stay deterministic.

If yes, name the seam (e.g. a `Fire*Async()` that advances the injected clock to the next due instant and drains) and the implementation requirement it implies: the production timer or reminder must arm against the injected `TimeProvider`, or the harness cannot drive it. Wall-clock delay is never the test mechanism — that constraint is a standing one, so the seam is the deliverable.

### 9. Conformance batteries — `Edict.Tests.Conformance` (ADR-0054)

Conformance is Edict's entire confidence story against real third-party tech, so a change with backend-dependent correctness that skips it ships unproven. It runs as two axis batteries (streaming + persistence, supersedes 0027): the streaming battery binds stream-sensitive scenarios against real Azure Queue + Kafka over dumb persistence; the persistence battery binds durability scenarios against real Azure Table + Postgres over dumb MemoryStreams. Each axis provider's `.Tests` project binds the shared scenarios, and the binding-completeness guard (`Edict.Architecture.Tests`, #296) fails if a scenario is added to the abstract battery but left unbound on an axis.

Does the change add behaviour whose correctness depends on a real backend — durability across reactivation, redelivery, claim-check fetch, dead-letter persistence, ordering? If it would pass on in-memory fakes yet could break on a real queue or a real table, it needs a conformance scenario, not just a unit test.

If yes, capture which axis it belongs to (streaming or persistence — pick the axis whose real provider exercises the risk), the scenario shape, and the providers it binds across. Adding the abstract scenario *obliges* binding it on every provider on that axis or the build goes red — that obligation is the design requirement, captured now rather than discovered from the guard later. Note explicitly when a behaviour does **not** warrant a scenario (pure in-grain logic with no backend dependency), so the skip is a recorded decision.

### 10. Telemetry — `Edict.Telemetry` spans + `Meter` instruments (ADRs 0037-0040)

Spans are *the* observability mechanism — CLAUDE.md forbids log-narrating the command/event flow — so a behavioural change that opens no span and bumps no counter is invisible in production. Every span name, tag key, and meter name lives in one place, `SemanticConventions`, against the single `"Edict"` `ActivitySource`; a new step that emits telemetry ad hoc, off that registry, is the failure mode.

Does the change add a step a consumer needs to *see* happen (a new outbox effect, saga transition, claim-check hop, dead-letter cause, table write) or a quantity they need to *count* (a new failure mode, a new lifecycle outcome, a new duration)?

- **Span side:** a new span name or tag goes on the `"Edict"` source via `SemanticConventions`, span-name constants prefix-only so substrate `StartsWith` assertions keep holding. If the new tag carries a *consumer-supplied* value, the consumer-facing `[Telemeterized]` attribute and its `TelemeterizedMustBePrimitiveAnalyzer` guard (ADR 0037) are in play — that's an authoring-surface change, not internal-only.
- **Metric side:** a new instrument lands under the Meter naming rules (ADR 0038), and every tag must clear the cardinality policy (ADR 0039) — an unbounded dimension (raw `EventId`, route key, exception message, or anything not a compile-closed allowlist like `FailureReasonValues`) is the design-time catch, not a production cardinality blowup. An observable gauge needs the silo-local metrics cache seam (ADR 0040) so a scrape costs zero grain calls; that cache feed is an implementation requirement, capture it.

Capture the span/tag/meter name and its tags here. Note explicitly when a change is span-only, metric-only, or neither (pure plumbing a consumer never observes), so the skip is a recorded decision. The prose homes (`docs/usage/concepts/telemetry.md`, `docs/operations/observability.md`) are axis 5's job — anchor, don't duplicate.

### 11. Sample app — `Sample/*`

Does the change need a demo handler/saga/projection to exist in the Sample app, or extend one that already exists?

The Sample app is the sales-demo framing for strangers cloning the repo — the bar is "does a visitor see the new concept *used*?" If yes, that's a deliverable; capture which Sample project (`Sample.Web`, `Sample.Silo`, contract assemblies) needs the change.

## Rules

- **Ask one axis at a time.** Do not present the list. Walk it as part of the interview.
- **Capture the answer as a requirement.** Not a TODO, not a follow-up — a design requirement that travels with the PRD or issue when the grill closes.
- **Skip axes the change does not touch.** The point is the ask, not exhaustive paperwork.
- **Vocabulary anchors are pointers, not copies.** When an axis references an existing skill body, MCP tool, or doc page, the authoritative content lives there. Do not duplicate it here, and refresh this file's anchor list when the surface itself moves.
