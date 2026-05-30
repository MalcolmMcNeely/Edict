# Skills walkthrough

Five skills ship in the `Edict.ClaudeSkills` bundle: `edict-authoring`, `edict-contracts`, `edict-silo-wiring`, `edict-testing`, `edict-diagnostics`. Each fires on a different edit shape and prescribes the MCP tool an Edict-aware agent should reach for next. This page shows what that loop feels like in practice on two concrete scenarios, calls out the silo-wiring story separately for `Program.cs`-only readers, and ends with a per-skill catalogue.

Every JSON snippet below is taken verbatim from `McpToolStdioRoundtripTests` running real `edict-mcp` against a consumer-shaped fixture. Every C# snippet is taken verbatim from the `FixtureLibrary.WithSubmitOrder/` fixture project. The test snippet is taken verbatim from `FixtureLibrary.WithSubmitOrder.Tests/`, where it boots `EdictTestApp` and asserts on the Timeline.

## Scenario A — Add a SubmitOrder command that closes an open order and raises OrderSubmitted

A consumer working on the `FixtureLibrary` Orders aggregate decides to add a `SubmitOrder` command. The aggregate already handles `PlaceOrderCommand` and `CancelOrderCommand` and raises `OrderPlaced`. The new behaviour should refuse a second submission and raise an `OrderSubmittedEvent` on the existing `Orders` stream.

### edict-authoring fires — handler inventory before code

The intent to add a new Command Handler matches the `edict-authoring` trigger ("adding a new feature — a new Command, Event, Command Handler, Event Handler, Saga, Projection Builder, or Table Projection Builder"). The skill body prescribes a single load-bearing move: call `edict_list_handlers` and `edict_list_route_keys` **before** writing code, to catch a duplicate handler or a Guid-key collision.

`edict_list_handlers` returns the existing aggregate's inventory:

```json
{
  "handlers": [
    {
      "declaringTypeName": "FixtureLibrary.Activity.OrderActivityProjection",
      "role": "projectionBuilder",
      "boundContracts": [
        {
          "fullTypeName": "FixtureLibrary.Orders.OrderPlaced",
          "routeKeyPropertyName": "OrderId"
        }
      ],
      "declaringAssembly": "FixtureLibrary",
      "sourceLocation": {
        "filePath": "FixtureLibrary/Activity/OrderActivityProjection.cs",
        "line": 6,
        "column": 1
      }
    },
    {
      "declaringTypeName": "FixtureLibrary.Notifications.OrderPlacedEmailHandler",
      "role": "eventHandler",
      "boundContracts": [
        {
          "fullTypeName": "FixtureLibrary.Orders.OrderPlaced",
          "routeKeyPropertyName": "OrderId"
        }
      ],
      "declaringAssembly": "FixtureLibrary",
      "sourceLocation": {
        "filePath": "FixtureLibrary/Notifications/OrderPlacedEmailHandler.cs",
        "line": 6,
        "column": 1
      }
    },
    {
      "declaringTypeName": "FixtureLibrary.Orders.OrderCommandHandler",
      "role": "commandHandler",
      "boundContracts": [
        {
          "fullTypeName": "FixtureLibrary.Orders.CancelOrderCommand",
          "routeKeyPropertyName": "OrderId"
        },
        {
          "fullTypeName": "FixtureLibrary.Orders.PlaceOrderCommand",
          "routeKeyPropertyName": "OrderId"
        }
      ],
      "declaringAssembly": "FixtureLibrary",
      "sourceLocation": {
        "filePath": "FixtureLibrary/Orders/OrderCommandHandler.Cancel.cs",
        "line": 3,
        "column": 1
      }
    },
    {
      "declaringTypeName": "FixtureLibrary.Reporting.OrdersByStatusProjection",
      "role": "tableProjectionBuilder",
      "boundContracts": [
        {
          "fullTypeName": "FixtureLibrary.Orders.OrderPlaced",
          "routeKeyPropertyName": "OrderId"
        }
      ],
      "declaringAssembly": "FixtureLibrary",
      "sourceLocation": {
        "filePath": "FixtureLibrary/Reporting/OrdersByStatusProjection.cs",
        "line": 5,
        "column": 1
      }
    },
    {
      "declaringTypeName": "FixtureLibrary.Shipping.ShipmentSaga",
      "role": "saga",
      "boundContracts": [
        {
          "fullTypeName": "FixtureLibrary.Orders.OrderPlaced",
          "routeKeyPropertyName": "OrderId"
        }
      ],
      "declaringAssembly": "FixtureLibrary",
      "sourceLocation": {
        "filePath": "FixtureLibrary/Shipping/ShipmentSaga.cs",
        "line": 6,
        "column": 1
      }
    }
  ],
  "driftStatus": "no-edict-references"
}
```

`OrderCommandHandler` already exists; the right move is to extend it with a third `Handle` overload, not to write a parallel handler. `edict_list_route_keys` then groups by Command/Event so the `OrderId` key is visible at a glance:

```json
{
  "commands": [
    {
      "commandType": "FixtureLibrary.Orders.CancelOrderCommand",
      "routeKeyProperty": "OrderId",
      "handlers": [
        "FixtureLibrary.Orders.OrderCommandHandler"
      ]
    },
    {
      "commandType": "FixtureLibrary.Orders.PlaceOrderCommand",
      "routeKeyProperty": "OrderId",
      "handlers": [
        "FixtureLibrary.Orders.OrderCommandHandler"
      ]
    }
  ],
  "events": [
    {
      "eventType": "FixtureLibrary.Orders.OrderPlaced",
      "routeKeyProperty": "OrderId",
      "subscribers": [
        "FixtureLibrary.Activity.OrderActivityProjection",
        "FixtureLibrary.Notifications.OrderPlacedEmailHandler",
        "FixtureLibrary.Reporting.OrdersByStatusProjection",
        "FixtureLibrary.Shipping.ShipmentSaga"
      ]
    }
  ],
  "collisions": [],
  "driftStatus": "no-edict-references"
}
```

`SubmitOrderCommand` will reuse `OrderId` as its route key — same aggregate, same Guid, no collision. `collisions: []` confirms no existing Command is bound to more than one handler in the solution.

### edict-contracts fires — glossary lookup, then the contract files

Writing the new contracts trips `edict-contracts` ("defining or modifying a Command or Event contract"). The new Event needs an `[EdictStream]` attribute, and the consumer is unsure whether `OrderSubmittedEvent` joins the existing `Orders` stream or gets a new one. The skill body prescribes `edict_describe_glossary_term` for fuzzy term lookup; querying `"Stream"`, `"Domain Stream"`, or `"EdictStream"` all resolve to the same entry because the `Edict` prefix and the `Domain` qualifier are elidable:

```markdown
**Domain Stream**:
A named Orleans stream that carries every event type for one domain, declared once via `[Stream("Name")]` on the concrete event type.
_Avoid_: per-event-type streams; inferring the stream name from the CLR namespace; a publisher and subscriber naming the stream independently.
```

The `_Avoid_` list answers the question directly: a per-event-type stream is the anti-pattern, so `OrderSubmittedEvent` joins the existing `Orders` stream. The contract files land as:

```csharp
using Edict.Contracts.Commands;

namespace FixtureLibrary.WithSubmitOrder.Orders;

public sealed partial record SubmitOrderCommand : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; }
}
```

```csharp
using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace FixtureLibrary.WithSubmitOrder.Orders;

[EdictStream("Orders")]
public sealed partial record OrderSubmittedEvent : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; }
}
```

A third partial of `OrderCommandHandler` holds the new `Handle` method. The aggregate's `State` carries an `IsSubmitted` flag; a second submission is rejected, not silently re-raised:

```csharp
using Edict.Contracts.Commands;

namespace FixtureLibrary.WithSubmitOrder.Orders;

public sealed partial class OrderCommandHandler
{
    public Task<EdictCommandResult> Handle(SubmitOrderCommand command)
    {
        if (State.IsSubmitted)
        {
            return Task.FromResult<EdictCommandResult>(
                new EdictCommandResult.Rejected(new[]
                {
                    new EdictRejectionReason("already_submitted", "Order has already been submitted."),
                }));
        }

        State.IsSubmitted = true;
        Raise(new OrderSubmittedEvent { OrderId = command.OrderId });
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
```

### edict-testing fires — no MCP call, prescription-only

Writing the test trips `edict-testing` ("writing tests against an Edict consumer app — anything spinning up EdictTestApp"). This skill makes **no MCP-tool call**. Its body is loaded as context and prescribes the test shape directly: boot `EdictTestApp`, send Commands, `await app.Drain()`, assert on the `Timeline`. No mocks of Orleans, no `IEdictSender` stub, no `Task.Delay`, no escape hatch around chaos delivery.

```csharp
using Edict.Testing;

using FixtureLibrary.WithSubmitOrder.Orders;

using Xunit;

namespace FixtureLibrary.WithSubmitOrder.Tests;

public sealed class SubmitOrderRoundtripTests
{
    [Fact]
    public async Task PlaceThenSubmit_ClosesTheOrder_AndRaisesOrderPlacedAndOrderSubmitted()
    {
        var orderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await using var app = await EdictTestApp.StartAsync(builder => builder
            .WithConsumer(typeof(SubmitOrderCommand).Assembly));

        await app.Send(new PlaceOrderCommand { OrderId = orderId });
        await app.Send(new SubmitOrderCommand { OrderId = orderId });
        await app.Drain();

        var raisedEvents = app.Timeline.Entries
            .Where(entry => entry.Kind == "Event")
            .Select(entry => entry.Type)
            .ToArray();

        Assert.Contains(nameof(OrderPlaced), raisedEvents);
        Assert.Contains(nameof(OrderSubmittedEvent), raisedEvents);
    }
}
```

The whole loop — inventory check, term lookup, contract files, handler, test — is what `edict-authoring` → `edict-contracts` → `edict-testing` prescribes. The MCP tools surface what the consumer's solution actually contains; the skill bodies say when to call them and what to do with the result.

## Scenario B — An InvokeHandler dead-letter row appeared

An `EdictDeadLetterRaised` row with `Kind = InvokeHandler` has shown up in production. The consumer wants to know what to do about it.

### edict-diagnostics fires — repository read first, then the ADR lookup

The query about a dead-letter row matches `edict-diagnostics` ("investigating a runtime failure — a missing event, a dead-letter row, a stuck saga, a projection that never updated, or a trace that does not stitch"). The skill body prescribes `IEdictDeadLetterRepository.ListAsync` as the read surface (never the underlying table) and points at the captured W3C `TraceParent` as the way to stitch the chain to the originating Command. For any "why does dead-letter behave this way?" question the skill prescribes `edict_lookup_adr` — for an `InvokeHandler` failure the relevant decision is ADR-0018:

```markdown
# Dead letter (forensic-only, table-projection-backed)

Dead-lettering is **observability, not back-pressure**: when an Outbox entry exhausts `MaxAttempts`, the engine — in the same one grain-state write — removes the failed entry from the Outbox slice and appends a new `PublishEvent` entry carrying an `EdictDeadLetterRaised` event; a built-in singleton `EdictDeadLetterProjectionBuilder` (auto-wired by `AddEdict`) consumes that stream and upserts to a fleet-wide table with both per-aggregate and fleet-wide queries via the read-only `IEdictDeadLetterRepository`. There is **no cap**, **no in-grain dead-letter slice**, and **no `RedriveAsync`** — recovery is manual re-emission (re-invoke the source aggregate or saga; for `UpsertRow` permanent failure, the operator repairs the row by hand informed by the dead-letter projection). The atomicity guarantee is preserved (the failed-entry removal and the dead-letter publish are the same write); the dead-letter publish inherits the Outbox's at-least-once retry, trace continuity (the captured `traceparent` propagates onto the projection row), and standard window-based idempotency.

## Considered Options

- **Block-intake-on-cap with a per-aggregate `DeadLetter` slice and `RedriveAsync`** (the prior design) — superseded: a long downstream outage stopped the aggregate from accepting commands at all (back-pressure on the producer side of the outage, where the producer cannot do anything about it); every successful command paid the deserialization and write-amplification cost of carrying the slice; inspection was per-aggregate only, but the operator's first triage question during a system-wide failure is "what's broken fleet-wide?".
- **External / dedicated dead-letter store** — rejected: a new transport with its own SDK and retry semantics; the table-projection approach gives a familiar queryable read surface (ADR 0013).
- **Per-aggregate projection + global roll-up at v1** — rejected for v1, kept as the upgrade path: singleton hot-grain risk is bounded by the same Orleans stream provider every other event uses; ship the singleton first, see real consumer load before paying the design cost.
- **Per-effect-kind `MaxAttempts` / backoff** — rejected for v1: one global `MaxAttempts` and one backoff curve via `EdictOptions` matches "knobs with sensible defaults", not knobs per axis.

## Consequences

- Soundness downgrade: ADR 0015's claim that the Outbox closes the ADR-0011 double-apply gap stands **for the transient-failure case only**. Permanent `UpsertRow` failure now leaves the destination row missing and requires manual operator repair.
- The `EdictDeadLetterRaised` event is widened with optional fields populated only for `InvokeHandler` and `BlobMissing` failures (`SourceEventType`, `SourceEventId`, `ClaimCheckKey`, `FailureKind`) so the operator can query the projection without parsing payload bytes.
```

The decision answers the "should I redrive this?" question directly: there is no `RedriveAsync`, recovery for an `InvokeHandler` failure is manual re-emission of the source Event Handler's input, and the captured `TraceParent` is what stitches the chain to the originating Command in the consumer's observability stack.

### edict-diagnostics fires — workspace alignment when results look off

If a `IEdictDeadLetterRepository` query returns empty when rows obviously exist, or `edict_list_handlers` returns nothing when handlers are obviously present, the skill body prescribes `edict_describe_mcp_state` before re-running the lookup. The tool reports the loaded solution path, the indexed-handler count, the Edict tool-vs-library version report, and the registered tool list — a mismatch between the reported solution and the consumer's actual workspace is the documented explanation:

```json
{
  "loadedSolutionPath": "{REPO_ROOT}/Edict/Edict.Mcp.Tests/Fixtures/TracerBulletFixture/TracerBulletFixture.slnx",
  "indexedHandlerCount": 5,
  "edictVersionReport": {
    "toolVersion": "{TOOL_VERSION}",
    "references": [],
    "isDrifted": false,
    "hasNoEdictReferences": true,
    "hasInconsistentLibraryVersions": false,
    "driftStatus": "no-edict-references"
  },
  "registeredTools": [
    {
      "name": "edict_describe_mcp_state",
      "description": "Self-diagnostic. Reports the loaded solution path, indexed-handler count, the Edict tool-vs-library version report, and the list of MCP tools the server has registered."
    },
    {
      "name": "edict_describe_glossary_term",
      "description": "Returns the Edict glossary entry for a term from CONTEXT.md, including its definition, the \u0027_Avoid_\u0027 list, and any inline cross-references. Case-insensitive; the optional \u0027Edict\u0027 prefix on the query is elidable."
    },
    {
      "name": "edict_lookup_adr",
      "description": "Returns the raw markdown body of an Edict ADR matching the query. The query is either an ADR number (\u002728\u0027 or \u00270028\u0027) or a fuzzy substring of the ADR title."
    },
    {
      "name": "edict_list_handlers",
      "description": "Returns every consumer-defined subclass of EdictCommandHandler / EdictEventHandler / EdictSaga / EdictProjectionBuilder / EdictTableProjectionBuilder in the loaded solution, each with its role, bound Command/Event types, [EdictRouteKey] property name, declaring assembly, and source location."
    },
    {
      "name": "edict_list_route_keys",
      "description": "Derived view over the handler inventory. Groups Commands by their handler classes (a Command bound to more than one handler is a collision) and Events by their subscriber classes, with the [EdictRouteKey] property name on each contract."
    },
    {
      "name": "edict_describe_silo_wiring",
      "description": "Locates Program.cs in the loaded solution, walks the ISiloBuilder invocation chain, and reports the AddEdict* extensions that are wired plus the known-but-missing ones an agent should consider before suggesting wiring changes (for example AddEdictAzureBlobClaimCheck when the consumer asks for a Claim Check setup)."
    }
  ]
}
```

`loadedSolutionPath` is the single field that ends most "why am I seeing nothing?" investigations. The `--solution` override in `.mcp.json` is the documented fix when it points at the wrong workspace; [troubleshooting.md](troubleshooting.md) covers the version-drift case where `isDrifted` flips true.

## Silo-wiring callout

When you edit `Program.cs` or any other silo-wiring file, `edict-silo-wiring` fires ("editing Program.cs or any silo wiring file — anywhere the AddEdict* extension chain is being assembled or changed"). Its body prescribes one move before any wiring change: call `edict_describe_silo_wiring`. The tool locates `Program.cs`, walks the `ISiloBuilder` invocation chain, and returns two arrays — `wired` and `missing` — so the agent picks the right `AddEdict*` extension instead of guessing the silo's substrate from grep. The classic example: a consumer asks for a Claim Check setup, the tool reports `AddEdictAzureBlobClaimCheck` in `missing`, and the agent suggests that exact extension. `edict-silo-wiring` is the entry point for `Program.cs`-only readers — you do not need to read Scenario A first.

## Per-skill catalogue

### edict-authoring

- **Trigger** — Working on an Edict consumer app and adding a new feature: a Command, Event, Command Handler, Event Handler, Saga, Projection Builder, or Table Projection Builder.
- **MCP tools** — `edict_list_handlers` and `edict_list_route_keys` (load-bearing, called before any code is written); `edict_describe_glossary_term` for fuzzy role-name lookup ("what is a Saga?", "what does Projection Builder mean here?").
- **Guards against** — Writing a parallel handler for a Command that already has one; minting a `[EdictRouteKey]` Guid that already routes a different Command (a silent runtime collision); guessing a role definition from its name; using the framework-reserved `Edict` brand prefix on a consumer subclass.

### edict-contracts

- **Trigger** — Working on an Edict consumer app and defining or modifying a Command or Event contract — anything deriving from `EdictCommand` or `EdictEvent`.
- **MCP tools** — `edict_describe_glossary_term` for contract-term lookup (`Domain Stream`, `Route Key`, `Telemeterized`); `edict_lookup_adr` (load-bearing) for the wire-format "why" questions (MessagePack-first, `[Alias]`, no `[Union]`).
- **Guards against** — Using `[Key]` instead of `[EdictRouteKey]` (collides with `System.ComponentModel.DataAnnotations`); omitting `[EdictStream]` (`EDICT008` at build time); reaching for `[Union]` instead of splitting into two Commands; deriving a consumer Event from `EdictEventEnvelope`.

### edict-silo-wiring

- **Trigger** — Working on an Edict consumer app and editing `Program.cs` or any silo wiring file — anywhere the `AddEdict*` extension chain is being assembled or changed.
- **MCP tools** — `edict_describe_silo_wiring` (load-bearing, called before any wiring change).
- **Guards against** — Proposing the wrong `AddEdict*` extension because the silo's substrate was guessed from grep; mixing two streaming providers or two persistence providers; wiring streaming or persistence extensions on the client; missing `AddEdictAzureBlobClaimCheck` on the Azure streaming pairing.

### edict-testing

- **Trigger** — Working on an Edict consumer app and writing tests against an Edict consumer app — anything spinning up `EdictTestApp`, asserting on the Timeline, probing a saga or projection, or swapping a consumer-injected collaborator.
- **MCP tools** — None. This skill is prescription-only; its body is loaded as context and guides the test shape directly.
- **Guards against** — Mocking Orleans, `IEdictSender`, a Saga, a Projection Builder, or a Command Handler (those are the code under test); reaching for `Task.Delay` instead of `await app.Drain()` or `await app.AdvanceClock(...)`; assuming an event order stricter than at-least-once (chaos delivery is on by default and not configurable); reading the `deadletter` table directly instead of via `GetProjectionRow`.

### edict-diagnostics

- **Trigger** — Working on an Edict consumer app and investigating a runtime failure — a missing event, a dead-letter row, a stuck saga, a projection that never updated, or a trace that does not stitch.
- **MCP tools** — `edict_lookup_adr` (load-bearing) for the "why does dead-letter / Outbox / claim-check behave this way?" questions (ADR-0015, 0018, 0019, 0020, 0003, 0041); `edict_describe_mcp_state` (load-bearing) when MCP results look off (empty handler list, missing dead-letter rows).
- **Guards against** — Treating dead-lettering as a retry/redrive mechanism (it is forensic-only); reading the underlying `deadletter` table directly instead of via `IEdictDeadLetterRepository`; stitching causality via the Command/Event Guid instead of the captured `TraceParent`; re-running an MCP lookup against a misaligned workspace without checking `loadedSolutionPath` first.

## See also

- [Setup](setup.md) — combined install recipe.
- [MCP tools](mcp-tools.md) — per-tool reference with input/output contracts.
- [Troubleshooting](troubleshooting.md) — version drift, `edict_describe_mcp_state`, standalone use for non-Claude editors.
- ADRs — [0044 — Agentic tooling](../../adr/0044-agentic-tooling.md).
