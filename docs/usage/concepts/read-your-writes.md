# Read-your-writes

In CQRS the write side and the read side are separated by an async hop: a Command is accepted, raises an Event, the Event flows through the Outbox and the stream to a Projection Builder, and only then is the read-model row written. Between "Accepted" and "the row is there" is a window — usually short, sometimes not — where the user has placed the order but the projection does not yet show it. Read-your-writes closes that window: a Command's result carries a cursor, and a Projection read can wait on it until the work the Command set in motion is visible.

```csharp
EdictCommandResult result = await sender.SendAsync(new CheckoutCartCommand(cartId));

if (result is EdictCommandResult.Accepted accepted)
{
    EdictProjectionRead<CheckedOutCartRow> read =
        await checkedOutCarts.GetAsync(cartId.ToString(), "cart", after: accepted.Cursor);

    // read.Status is CursorReached and read.Row reflects the checkout — on this
    // same call, no poll, no retry loop.
}
```

With no cursor the read answers immediately from current store state — the plain poll path. With the cursor from `Accepted` it waits, briefly and boundedly, until the named conversation has been applied to the projection, then serves the row.

## The cursor and the conversation it names

`EdictCommandResult.Accepted` carries an `EdictCursor`, an opaque wrapper over a chain-stable **Conversation Id** the framework stamps. The conversation id is minted when a Command is first sent and then rides every message that Command sets in motion: the Events it raises, and any Command a Saga dispatches in reaction to those Events. So the cursor a `CheckoutCartCommand` returns also names the Order a bridge Saga places downstream, not just the checked-out-cart row — a read with that cursor waits for whichever projection effect it is pointed at.

A time trigger (a timer, an `EdictSchedule` fire, a saga cap) has no upstream message, so it mints a fresh conversation. The cursor is not the per-message `[EdictRouteKey]` Guid (that re-keys across domains) and not W3C trace context (which is null when unsampled); it is a dedicated identifier that exists precisely so this feature is correct.

You almost never author a cursor by hand: keep returning `new EdictCommandResult.Accepted()` from handlers and the runtime stamps the real cursor after the handler returns. `EdictCursor` has a public constructor only for the rare caller who supplied its own conversation id and wants to build a cursor without round-tripping `Accepted`.

## The three read modes

Both reader methods take an optional `after` cursor and an optional `timeout`, and those two parameters express every mode:

| `after` | `timeout` | Behaviour | Resulting `EdictReadStatus` |
| --- | --- | --- | --- |
| `null` | (ignored) | Answers immediately from current store state — the poll path. | `Immediate` |
| a cursor | omitted or a `TimeSpan` | Waits up to the bound, then answers. An omitted timeout falls back to `EdictOptions.ProjectionReadTimeout`. | `CursorReached` or `CursorTimedOut` |
| a cursor | `Timeout.InfiniteTimeSpan` | Waits indefinitely until the conversation is visible. | `CursorReached` (or caller cancellation) |

Indefinite waiting must be **explicit**: an omitted timeout on a cursor read is always bounded, never infinite, so a forgotten timeout cannot hang a request. This mirrors `EdictSchedule.Unbounded` — opting out of a bound is a deliberate, visible choice.

## The result is a tri-state, never a throw

A Projection read returns `EdictProjectionRead<TRow>` (point-get) or `EdictProjectionPartitionRead<TRow>` (partition query), each carrying the row(s) and an `EdictReadStatus`:

- **`Immediate`** — no cursor was supplied; the read answered from current store state.
- **`CursorReached`** — the cursor's conversation is visible on this projection.
- **`CursorTimedOut`** — the bounded wait elapsed first. The latest available row is **still returned**, flagged, so you decide whether to render it, show a "still catching up" hint, or re-read.

Eventual-consistency lag is an expected outcome, so it is a status on the result, not a thrown exception — the same reasoning that makes business rejection a `Rejected` result rather than a throw. The only exception a read raises is `OperationCanceledException`, when the caller cancels. A cursor for a conversation that has aged out of the projection's window degrades to a plain immediate read, not an error.

## `CursorReached` is any-applied

When one conversation produces several Events to the *same* projection, `CursorReached` is recorded on the **first** applied. The honest contract is: *`CursorReached` means at least the conversation's first effect on this projection is visible* — not that every effect has landed. Where exact read-your-writes matters, prefer one Event per Command, so the conversation has a single effect on the projection and "first applied" equals "fully applied". This is guidance, not a rule the framework enforces (enforcing it would break legitimate multi-`Raise` batch publish).

## How the wait works

The read routes through the Projection Builder grain (see [projections.md](projections.md)), so the activation that owns the rows is on the read path and can park the wait. The grain keeps a bounded, persisted ring of recently processed conversation ids — modelled on the dedup ring, sized by `EdictOptions.CorrelationWindowSize` (default 100) — that advances on the same commit as the dedup ring, so it costs no extra write and survives a deactivate/reactivate. The "is this conversation processed" check the wait consults is marked only *after* the row write has drained to the store, so a `CursorReached` answer implies the row is readable. A read that arrives before the write has drained parks as a waiter and is signalled at end-of-turn once the row lands.

## Surface

- **`EdictCursor`** (`Edict.Contracts.Commands`) — `readonly record struct EdictCursor(Guid ConversationId)`. Echoed on `EdictCommandResult.Accepted`.
- **`IEdictProjectionReader<TProjection>.ReadAsync(key, after, timeout, cancellationToken)`** (the in-grain State species) and **`IEdictListProjectionReader<TListProjection>.GetAsync(partitionKey, rowKey, after, timeout, cancellationToken)`** / **`QueryPartitionAsync(partitionKey, after, timeout, cancellationToken)`** (the external List species) (`Edict.Contracts.Projections`) — `after` and `timeout` are optional; omit both for a plain read. See [projections.md](projections.md).
- **`EdictProjectionRead<TRow>`** / **`EdictProjectionPartitionRead<TRow>`** (`Edict.Contracts.Projections`) — the tri-state results.
- **`EdictReadStatus`** (`Edict.Contracts.Projections`) — `Immediate`, `CursorReached`, `CursorTimedOut`.
- **`EdictOptions.CorrelationWindowSize`** and **`EdictOptions.ProjectionReadTimeout`** — see [core.md](../../configuration/core.md).

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Conversation Id`, `EdictCursor`, `Projection Read`, `Projection Reader`, `Command Result`.
- Concepts — [projections.md](projections.md), [idempotency.md](idempotency.md), [events.md](events.md).
- Configuration — [core.md](../../configuration/core.md) — `CorrelationWindowSize`, `ProjectionReadTimeout`.
- ADR — [0058 — Read-your-writes via correlation cursor](../../adr/0058-read-your-writes-via-correlation-cursor.md), [0057 — Projection reads through the grain](../../adr/0057-projection-reads-through-the-grain.md).
