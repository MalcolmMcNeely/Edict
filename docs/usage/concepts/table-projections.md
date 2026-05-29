# Table projections

An `EdictTableProjectionBuilder<T>` keeps its read model in an external composite-key store, so grain activation stays small regardless of how large the read model grows. The row write is committed atomically with the dedup ring in one grain-state write, then drained at-least-once.

```csharp
using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

public sealed partial class OrdersByStatusTableProjectionBuilder
    : EdictTableProjectionBuilder<OrderStatusRow>
{
    public OrdersByStatusTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "ordersbystatus";

    protected override string GetRowKey(EdictEvent edictEvent) => "status";

    public Task Handle(OrderPlacedEvent edictEvent)
    {
        CurrentRow.Status = "Open";
        CurrentRow.PlacedAt = edictEvent.OccurredAt;
        return Task.CompletedTask;
    }
}
```

The application reads the projection through `IEdictTableRepository<T>`, never by talking to the store directly:

```csharp
OrderStatusRow? row = await tableRepository.GetAsync(orderId.ToString(), "status");
```

## Surface

- **`EdictTableProjectionBuilder<T>`** (`Edict.Core.Projections`) where `T : class, IEdictPersistedState, new()`. The row type `T` is the persistence-neutral shape of the read model — it must not carry storage-provider types (no `ITableEntity`, no DynamoDB row types).
- **`TableName`** (`protected abstract string`) — the provider-specific table or collection name.
- **`GetRowKey(EdictEvent edictEvent)`** (`protected abstract string`) — derives the row key from the incoming event.
- **`DefaultPartitionKey`** (`protected virtual string`) — defaults to the grain's primary key as a string (which equals the event's `[EdictRouteKey]` Guid for per-aggregate projections). Override for global-singleton projections that collapse every row into one partition.
- **`CurrentRow`** (`protected T`) — the row loaded (or freshly constructed) before each `Handle` call. Modifications captured into an `UpsertRow` outbox effect after the handler returns. The setter is `protected` so an `init`-only row type can be replaced wholesale.
- **`IEdictTableStoreFactory`** is the framework-internal write seam; ctor-inject and forward to `base`. The application tier reads via **`IEdictTableRepository<T>`** (`GetAsync`, `QueryPartitionAsync`); the repository is read-only.

The upsert is idempotent by `(PartitionKey, RowKey)` — at-least-once redelivery of the effect does not double-apply.

## Analyzer rules

- **EDICT001** — concrete table-projection builders must be declared `partial`.
- **EDICT009** — every `Handle` must return `Task` and take a single `EdictEvent`-derived parameter.
- **EDICT011** — the row type `T` implements `IEdictPersistedState` and must carry `[GenerateSerializer]`, `[Alias("literal")]`, and `[Id(n)]` on every declared public property.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Table Projection Builder`, `Table Repository`, `Projection Builder`, `Outbox`.
- Concepts — [projection-builders.md](projection-builders.md), [events.md](events.md), [idempotency.md](idempotency.md), [dead-letter.md](dead-letter.md).
