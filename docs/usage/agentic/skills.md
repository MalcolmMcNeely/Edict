# Skills walkthrough

Five skills ship in `Edict.ClaudeSkills`: `edict-authoring`, `edict-contracts`, `edict-silo-wiring`, `edict-testing`, `edict-diagnostics`. Each fires on a different edit shape and prescribes the MCP tool the agent should reach for first. This page shows the loop on two concrete scenarios, calls out the silo-wiring story for `Program.cs`-only readers, and ends with a per-skill reference.

## Scenario A — Add a `SubmitOrderCommand` to an existing Orders aggregate

A consumer asks: "Add a `SubmitOrder` command to the Orders aggregate — it should refuse a second submission and raise `OrderSubmittedEvent` on the existing `Orders` stream."

The aggregate already handles `PlaceOrderCommand` and `CancelOrderCommand` and raises `OrderPlaced`.

### 1. `edict-authoring` fires — inventory before code

Adding a new Command Handler matches `edict-authoring`. The skill prescribes `edict_list_handlers` and `edict_list_route_keys` **before** any code is written, so an existing handler or a Guid-key collision shows up first.

`edict_list_handlers` returns one entry per consumer subclass with its role, bound contracts, route-key property, and source location. The response names `OrderCommandHandler` already bound to `PlaceOrderCommand` and `CancelOrderCommand` — extend that partial, don't write a parallel handler. `edict_list_route_keys` groups by Command/Event and reports `collisions: []`, so reusing `OrderId` is safe.

### 2. `edict-contracts` fires — glossary first

The new Event needs `[EdictStream]` and the consumer is unsure whether it joins the existing stream. `edict_describe_glossary_term` with `"Domain Stream"` returns the entry whose `_Avoid_` list names per-event-type streams as the anti-pattern — `OrderSubmittedEvent` joins the existing `Orders` stream.

```csharp
[EdictStream("Orders")]
public sealed partial record OrderSubmittedEvent : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; }
}
```

A third partial of `OrderCommandHandler` holds the new `HandleAsync` overload. A second submission returns `EdictCommandResult.Rejected`, not silently re-raise.

### 3. `edict-testing` fires — prescription only, no MCP call

`edict-testing` makes no MCP-tool call — its body is loaded as context and prescribes the test shape: boot `EdictTestApp`, send Commands, `await app.Drain()`, assert on `Timeline`. No mocks of Orleans or `IEdictSender`, no `Task.Delay`, no escape hatch around chaos delivery.

```csharp
await using var app = await EdictTestApp.StartAsync(builder => builder
    .WithConsumer(typeof(SubmitOrderCommand).Assembly));

await app.SendAsync(new PlaceOrderCommand { OrderId = orderId });
await app.SendAsync(new SubmitOrderCommand { OrderId = orderId });
await app.Drain();

Assert.Contains(nameof(OrderSubmittedEvent),
    app.Timeline.Entries.Where(e => e.Kind == "Event").Select(e => e.Type));
```

That's the full loop. Skills say when to query; MCP tools return what the solution actually contains; the agent edits with grounded context.

## Scenario B — An `InvokeHandler` dead-letter row appeared

A consumer asks: "There's an `EdictDeadLetterRaised` row with `Kind = InvokeHandler` in production — what do I do?"

### `edict-diagnostics` fires — read surface, then the ADR

The query matches `edict-diagnostics`. The skill prescribes `IEdictDeadLetterRepository.ListAsync` as the read surface (never the underlying table) and `edict_lookup_adr` for the "why" — for an `InvokeHandler` failure that's ADR-0018.

ADR-0018 answers the question directly: dead-lettering is forensic-only, there is no `RedriveAsync`, and recovery for an `InvokeHandler` failure is manual re-emission of the source Event Handler's input. The captured `TraceParent` is what stitches the chain to the originating Command in the consumer's observability stack. The atomicity guarantee — failed-entry removal and dead-letter publish are the same grain-state write — is preserved.

### When results look wrong — `edict_describe_mcp_state`

If `IEdictDeadLetterRepository.ListAsync` returns empty when rows obviously exist, the skill prescribes `edict_describe_mcp_state` before re-running the lookup. The response includes the loaded solution path, indexed-handler count, the Edict tool-vs-library version report, and the list of registered tools. `loadedSolutionPath` is the single field that ends most "why am I seeing nothing?" investigations — a mismatch with the consumer's actual workspace is the documented explanation, and the `--solution` override in `.mcp.json` is the fix. [troubleshooting.md](troubleshooting.md) covers the version-drift case.

## Silo-wiring callout

`edict-silo-wiring` fires on any `Program.cs` or silo-wiring edit. Its body prescribes one move first: call `edict_describe_silo_wiring`. The tool returns `wired` and `missing` arrays, so an agent asked to "set up a Claim Check" picks `AddEdictAzureBlobClaimCheck` from `missing` instead of guessing the silo's substrate from grep. This is the entry point if you only edit `Program.cs`.

## Per-skill reference

### edict-authoring
- **Fires on** — adding a Command, Event, or any handler / saga / projection class.
- **Tools** — `edict_list_handlers` and `edict_list_route_keys` (load-bearing, before writing code); `edict_describe_glossary_term` for role-name lookups.
- **Guards against** — parallel handlers; `[EdictRouteKey]` Guid collisions; consumer subclasses using the reserved `Edict` prefix.

### edict-contracts
- **Fires on** — defining or modifying anything deriving from `EdictCommand` or `EdictEvent`.
- **Tools** — `edict_describe_glossary_term`; `edict_lookup_adr` (load-bearing) for wire-format "why" questions.
- **Guards against** — `[Key]` instead of `[EdictRouteKey]`; missing `[EdictStream]` (`EDICT008`); reaching for `[Union]`; deriving from `EdictEventEnvelope`.

### edict-silo-wiring
- **Fires on** — editing `Program.cs` or any silo-wiring file.
- **Tools** — `edict_describe_silo_wiring` (load-bearing, before any wiring change).
- **Guards against** — wrong `AddEdict*` extension; mixed streaming or persistence providers; streaming/persistence wired on the client; missing `AddEdictAzureBlobClaimCheck` on the Azure pairing.

### edict-testing
- **Fires on** — writing tests against an Edict consumer app.
- **Tools** — none. Prescription-only.
- **Guards against** — mocking Orleans, `IEdictSender`, a Saga, or a Handler; `Task.Delay` instead of `await app.Drain()` / `await app.AdvanceClock(...)`; assuming stricter-than-at-least-once ordering; reading the `deadletter` table directly.

### edict-diagnostics
- **Fires on** — investigating a runtime failure (missing event, dead-letter row, stuck saga, broken trace).
- **Tools** — `edict_lookup_adr` (load-bearing) for behaviour questions; `edict_describe_mcp_state` (load-bearing) when results look off.
- **Guards against** — treating dead-letter as retry/redrive; reading the `deadletter` table directly; stitching causality via Guid instead of `TraceParent`; re-running lookups against a misaligned workspace.

## See also

- [Setup](setup.md) — install recipe.
- [MCP tools](mcp-tools.md) — per-tool reference.
- [Troubleshooting](troubleshooting.md) — version drift.
- ADRs — [0044 — Agentic tooling](../../adr/0044-agentic-tooling.md).
