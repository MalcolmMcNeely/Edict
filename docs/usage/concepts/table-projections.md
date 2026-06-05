# Table projections

An `EdictListProjectionBuilder<TRow>` keeps its read model in an external composite-key store, so grain activation stays small regardless of how large the read model grows. The row write is committed atomically with the dedup ring in one grain-state write, then drained at-least-once.

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

The application reads the projection through `IEdictProjectionReader<TRow>`, never by talking to the store directly. The read routes through the projection grain, so the read API carries no storage detail:

```csharp
OrderStatusRow? row = await projectionReader.GetAsync(orderId.ToString(), "status");
```

The partition key is the projection's routing key — for a per-aggregate projection this is the aggregate's `[EdictRouteKey]` Guid, exactly what you would pass to read its row.

## Surface

- **`EdictListProjectionBuilder<TRow>`** (`Edict.Core.Projections`) where `TRow : class, IEdictPersistedState, new()`. The row type `TRow` is the persistence-neutral shape of the read model — it must not carry storage-provider types (no `ITableEntity`, no DynamoDB row types).
- **`TableName`** (`protected abstract string`) — the provider-specific table or collection name.
- **`GetRowKey(EdictEvent edictEvent)`** (`protected abstract string`) — derives the row key from the incoming event.
- **`DefaultPartitionKey`** (`protected virtual string`) — defaults to the grain's primary key as a string (which equals the event's `[EdictRouteKey]` Guid for per-aggregate projections). Override for global-singleton projections that collapse every row into one partition.
- **`CurrentRow`** (`protected TRow`) — the row loaded (or freshly constructed) before each `HandleAsync` call. Modifications captured into an `UpsertRow` outbox effect after the handler returns. The setter is `protected` so an `init`-only row type can be replaced wholesale.
- **`IEdictTableStoreFactory`** is the framework-internal store seam; ctor-inject and forward to `base`. The application tier reads via **`IEdictProjectionReader<TRow>`** (`GetAsync`, `QueryPartitionAsync`), which routes through the grain; the reader is read-only and is registered automatically by `AddEdict()`.

The upsert is idempotent by `(PartitionKey, RowKey)` — at-least-once redelivery of the effect does not double-apply.

## Analyzer rules

- **EDICT001** — concrete table-projection builders must be declared `partial`.
- **EDICT009** — every `HandleAsync` must return `Task` and take a single `EdictEvent`-derived parameter.
- **EDICT011** — the row type `TRow` implements `IEdictPersistedState` and must carry `[GenerateSerializer]`, `[Alias("literal")]`, and `[Id(n)]` on every declared public property.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `List Projection Builder`, `Table Repository`, `Projection Builder`, `Outbox`.
- Concepts — [projection-builders.md](projection-builders.md), [events.md](events.md), [idempotency.md](idempotency.md), [dead-letter.md](dead-letter.md).
