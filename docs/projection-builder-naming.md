# Projection builder naming

## Decision

Two projection-builder species, named on consumer intent:

- **`EdictProjectionBuilder<TKey, TState>`** — canonical. Per-key state lives in grain state. Consumer mutates `State` inside `HandleAsync`; reads are by-key.
- **`EdictListProjectionBuilder<TRow>`** — variant. Queryable list of rows lives in an external composite-key store. Reads are by query.

```csharp
public sealed partial class CustomerProfile : EdictProjectionBuilder<Guid, CustomerState> { ... }
public sealed partial class OrdersByStatus : EdictListProjectionBuilder<OrdersByStatusRow> { ... }
```

`EdictListProjectionBuilder<TRow>` is the renamed form of the former `EdictTableProjectionBuilder<T>` (the rename has landed). Consumer subclasses drop the storage word and are named `{Name}ProjectionBuilder`; the base type, not a word in the class name, marks the species.

## Why these names

- **Consumer intent, not storage.** "List" says what the consumer is building (a queryable list). "Table" leaked Azure Table and put the concept on the wrong axis.
- **Canonical-unqualified, variant-qualified.** Matches .NET stdlib (`List<T>` unqualified, `LinkedList<T>` / `SortedList<T>` qualified). The per-key shape is the natural Orleans default — every other Edict consumer concept (command handlers, sagas, event handlers) is per-key — so the unqualified name belongs to the per-key projection.
- **Survives a third species.** A future `EdictFeedProjectionBuilder<TEntry>` slots in as another qualified variant; the canonical name stays put.

## Saga is not a projection builder

`EdictSaga<TProgress>` stores its `Progress` in grain state (in Azure: as an `edict-state` blob, single-blob ETag-atomic). That is the same persistence shape as the new `EdictProjectionBuilder<TKey, TState>`.

It still **should not** derive from `EdictProjectionBuilder`. They are distinct roles:

| | Saga | Projection Builder (keyed) |
|---|---|---|
| Purpose | Write-side coordinator | Read-side materialiser |
| Dispatches commands | Yes (exactly one per event) | No |
| CQRS side | Write | Read |

They already share the right root: `EdictIdempotencyBase<TPayload>`. Sibling species under it, not parent/child.

## Implementation note

The abstract marker base `EdictProjectionBuilder : EdictIdempotencyBase` is retained as the shared root: the generator and analyzer EDICT009 key on it, and `EdictListProjectionBuilder<TRow>` derives from it. C# arity-overloading lets the marker and a future `EdictProjectionBuilder<TKey, TState>` coexist, but that keyed species cannot inherit *through* the marker (the marker closes `EdictIdempotencyBase` over `EdictUnit`, the keyed species needs `EdictIdempotencyBase<TState>`). When the keyed species lands it slots in as a sibling under the marker root, not through it — resolved then, the same way the list species sits beside it now.
