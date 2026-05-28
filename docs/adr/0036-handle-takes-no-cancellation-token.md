# Handle takes no CancellationToken

Edict's consumer-facing `Handle(TEvent)` on event handlers, sagas, and projection builders deliberately omits a `CancellationToken` parameter — at-least-once delivery + per-consumer dedup (ADR-0002) + Outbox retry-and-dead-letter (ADR-0015/0018) is the cancellation/failure story. A handler that doesn't complete is not a correctness problem because the next delivery retries and the dedup ring suppresses double-effect; per-call timeouts belong at the side-effect boundary (`HttpClient.Timeout`, DB command timeout), not on the framework's dispatch seam.

## Considered Options

- **Widen `Handle(TEvent)` → `Handle(TEvent, CancellationToken)`** — rejected: Orleans's stream `IAsyncObserver.OnNextAsync` carries no CT, so the framework would have to synthesise and propagate a deactivation-linked token. The token only ever fires on grain shutdown, and a hanging handler is already redelivered by at-least-once + dedup, so the net value reduces to "slightly faster graceful drain on silo scale-down" against permanent consumer-surface widening.
- **Optional second overload** — rejected: two `Handle` shapes per consumer means two analyzer rules, two generated dispatch arms, and two patterns to document, all to dodge a one-time pre-release surface decision.
