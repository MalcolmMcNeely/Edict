# Projections

A Projection Builder consumes the live Event stream and maintains a current-state read model, processing the stream only forward. Edict is event-driven, not event-sourced, so there is no replay and no rebuild-from-history: a projection only ever sees events from the moment it is subscribed, forward. The dedup ring suppresses at-least-once redelivery per grain; see [idempotency.md](idempotency.md).

There are **two species** over one abstract root, `EdictProjectionBuilderBase<TPayload>`. You choose where a read model lives by which base you derive:

- A **State Projection Builder** (`EdictProjectionBuilder<TProjection>`) keeps the read model in the grain's own durable state.
- A **List Projection Builder** (`EdictListProjectionBuilder<TListProjection>`) keeps it in an external composite-key store.

The root owns everything the species share — the dedup ring, the implicit stream subscription, and the read-your-writes cursor — so the only thing that differs between them is where the read model is stored and how it commits.

## Choosing between the species

Pick by the size and shape of the read model, not by caution:

| | State Projection Builder | List Projection Builder |
|---|---|---|
| Read model lives in | the grain's durable state | an external keyed store |
| Best for | small, hot, **per-aggregate** state (one object per grain) | **large or unbounded** read models (many rows, queried by partition) |
| Commit | inline with the dedup ring in one grain-state write, no outbox effect | an `UpsertRow` outbox effect, drained at-least-once |
| Read-your-writes | resolves the instant the write lands | resolves once the effect drains to the store |
| Cost | the read model inflates grain activation latency | an external round trip on every read |

The State species is cheaper to commit and read, and read-your-writes is *tighter* because there is no asynchronous drain between commit and visibility. The price is activation latency: the whole read model loads with the grain, so keep it deliberately small. Reach for the List species the moment the read model is large, unbounded, or queried across many keys. Putting a durable read model in grain state "to be safe" is not a safety hedge — it is the State species' deliberate trade-off, and the wrong call for a read model that grows.

## State Projection Builder

The read model is the payload slot of the grain's persisted envelope. The consumer mutates a `Projection` accessor inside each `HandleAsync`, and that mutation rides the dedup-ring commit atomically. Structurally it is a saga without `Dispatch`: one grain holds one projection object.

```csharp
using Edict.Contracts.Events;
using Edict.Core.Projections;

public sealed partial class DeliveryStatusProjectionBuilder : EdictProjectionBuilder<DeliveryStatusRow>
{
    Task HandleAsync(DeliveryEtaTickedEvent edictEvent)
    {
        Projection.EtaDaysRemaining = edictEvent.EtaDaysRemaining;
        return Task.CompletedTask;
    }

    Task HandleAsync(DeliveredEvent edictEvent)
    {
        Projection.Delivered = true;
        return Task.CompletedTask;
    }
}
```

The application reads through `IEdictProjectionReader<TProjection>`, which routes through the projection grain — so the read API carries no storage detail. The key is the projection's routing key (the aggregate's `[EdictRouteKey]` Guid for a per-aggregate projection). `ReadAsync` returns the whole projection object; take `.Value`:

```csharp
DeliveryStatusRow? projection = (await projectionReader.ReadAsync(orderId)).Value;
```

## List Projection Builder

The read model lives in an external composite-key store, so grain activation stays small regardless of how large the read model grows. The consumer mutates `CurrentRow` inside each `HandleAsync`; the modified row is captured into an `UpsertRow` outbox effect after the handler returns, committed atomically with the dedup ring in one grain-state write, then drained at-least-once.

```csharp
using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

public sealed partial class OrdersByStatusProjectionBuilder
    : EdictListProjectionBuilder<OrderStatusRow>
{
    public OrdersByStatusProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "ordersbystatus";

    protected override string GetRowKey(EdictEvent edictEvent) => "status";

    Task HandleAsync(OrderPlacedEvent edictEvent)
    {
        CurrentRow.Status = "Open";
        CurrentRow.PlacedAt = edictEvent.OccurredAt;
        return Task.CompletedTask;
    }
}
```

The application reads through `IEdictListProjectionReader<TListProjection>`, never by talking to the store directly. The read routes through the projection grain, so the read API carries no storage detail. A point read returns an `EdictProjectionRead<TRow>` (the row plus a status); take `.Value`:

```csharp
OrderStatusRow? row = (await listProjectionReader.GetAsync(orderId.ToString(), "status")).Value;
```

A partition read returns an `EdictProjectionPartitionRead<TRow>`; take `.Rows`:

```csharp
IReadOnlyList<OrderStatusRow> rows = (await listProjectionReader.QueryPartitionAsync(orderId.ToString())).Rows;
```

The partition key is the projection's routing key — for a per-aggregate projection this is the aggregate's `[EdictRouteKey]` Guid. The upsert is idempotent by `(PartitionKey, RowKey)`, so at-least-once redelivery of the effect does not double-apply.

## Read-your-writes

Both readers support read-your-writes. Pass the `EdictCursor` from a Command's `Accepted` result as `after:` and the read waits, briefly and boundedly, until that Command's work is visible before answering. With no cursor the read answers immediately. See [read-your-writes.md](read-your-writes.md).

```csharp
DeliveryStatusRow? projection = (await projectionReader.ReadAsync(orderId, after: accepted.Cursor)).Value;
```

## Surface

- **`EdictProjectionBuilderBase<TPayload>`** (`Edict.Core.Projections`) — the abstract root both species derive from. Owns the dedup ring, the implicit stream subscription, and the read-your-writes cursor. A consumer never derives from it directly.
- **`EdictProjectionBuilder<TProjection>`** (`Edict.Core.Projections`) where `TProjection : IEdictPersistedState, new()` — the in-grain State species. Exposes a `protected TProjection Projection` accessor the handler mutates.
- **`EdictListProjectionBuilder<TListProjection>`** (`Edict.Core.Projections`) where `TListProjection : class, IEdictPersistedState, new()` — the external List species.
  - **`TableName`** (`protected abstract string`) — the provider-specific table or collection name.
  - **`GetRowKey(EdictEvent edictEvent)`** (`protected abstract string`) — derives the row key from the incoming event.
  - **`DefaultPartitionKey`** (`protected virtual string`) — defaults to the grain's primary key as a string (the event's `[EdictRouteKey]` Guid for per-aggregate projections). Override for global-singleton projections that collapse every row into one partition.
  - **`CurrentRow`** (`protected TListProjection`) — the row loaded (or freshly constructed) before each `HandleAsync` call; modifications are captured into an `UpsertRow` outbox effect after the handler returns. The setter is `protected` so an `init`-only row type can be replaced wholesale.
  - **`IEdictTableStoreFactory`** is the framework-internal store seam; ctor-inject and forward to `base`.
- The read-model POCO (`TProjection` / `TListProjection`) is the persistence-neutral shape of the read model — it must not carry storage-provider types (no `ITableEntity`, no DynamoDB row types).
- The application tier reads via **`IEdictProjectionReader<TProjection>`** (`ReadAsync`) for the State species and **`IEdictListProjectionReader<TListProjection>`** (`GetAsync`, `QueryPartitionAsync`) for the List species. Both are read-only, route through the grain, and are registered automatically by `AddEdict()`. Injecting the wrong species' reader for a projection type throws `EdictUnsupportedProjectionReadException` at runtime.

## Analyzer rules

- **EDICT001** — concrete projection builders (both species) must be declared `partial`; the generator emits the Orleans interface and the dispatch switch into a second partial declaration.
- **EDICT009** — every `HandleAsync` must return `Task` (not `Task<T>`) and take a single parameter that derives from `EdictEvent`.
- **EDICT011** — the read-model POCO implements `IEdictPersistedState` and must carry `[GenerateSerializer]`, a frozen-literal `[Alias("literal")]`, and `[Id(n)]` on every declared public property.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Projection Builder`, `State Projection Builder`, `List Projection Builder`, `Projection Reader`, `Projection Read`, `Outbox`.
- Concepts — [read-your-writes.md](read-your-writes.md), [events.md](events.md), [idempotency.md](idempotency.md), [dead-letter.md](dead-letter.md).
