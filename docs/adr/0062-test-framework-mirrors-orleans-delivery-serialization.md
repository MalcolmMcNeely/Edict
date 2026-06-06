# Test Framework delivery mirrors Orleans stream serialization

Status: Accepted

The in-memory Test Framework (`Edict.Testing`) replaces the Orleans memory-stream pulling agent with an in-process dispatcher (`InProcPublishExecutor`), because the real memory-stream agent does not deliver to referenced-assembly consumers. That dispatcher must reproduce Orleans's delivery semantics faithfully, or consumer tests pass against behaviour production never exhibits. The binding rule this ADR fixes in place: **the harness must not manufacture concurrency a real Orleans pulling agent does not produce.**

A real pulling agent delivers one stream's events to a single consumer activation serially. `RunConsumerCursor` awaits each `OnNextAsync` — the whole turn, including the grain-state commit — before pulling the next batch, and `OnNextAsync` is non-reentrant. So distinct events for one activation, and redeliveries of one event, are never in flight together; only different activations run concurrently, as they do across a real cluster. This is the production contract the harness has to honour.

The original harness diverged on exactly this point. It dispatched every delivery as a concurrent fire-and-forget task into a seam (`IEdictEventConsumer.OnEdictEventAsync`) marked `[AlwaysInterleave]`, so a single aggregate's events ran their handler turns concurrently on one projection activation. That interleaving leaked the read-your-writes cache's cumulative row snapshot (ADR-0058) across sibling events and, under the resulting `InconsistentStateException` write races, corrupted the durable count — a recurring CI flake (`DrainCoversEveryDispatchTests`, observed as both over- and under-count) long misdiagnosed as a production framework defect. It is not one: real delivery is serial, proven against real Azure Queue Streams and real Kafka, so the corruption is reachable only through the harness's manufactured concurrency. The deciding experiment ran the same handler-turn-overlap probe two ways — on the live harness it recorded five concurrent turns on one activation with a corrupted row; on real Azure Queue Streams the identical burst recorded a high-water mark of one and an exact count.

The fix makes the harness model the agent. `InProcPublishExecutor` serializes deliveries per `(grain class, route key)` activation: each delivery awaits the previous delivery to the same consumer before invoking the grain, mirroring the agent's await-then-next loop, while deliveries to different activations stay concurrent. A redelivery (a chaos duplicate) now follows the original rather than racing it — which also matches production, where an at-least-once redelivery is time-separated, not concurrent with the first attempt.

Two framework simplifications fall out, both of which were compensating for the manufactured concurrency rather than serving a real purpose:

- The `[AlwaysInterleave]` attribute is removed. It was never guarding reentrancy — the harness fan-out cascade is fire-and-forget, never awaited within a grain turn, so a saga or projection re-entering its own grain cannot deadlock (removing it leaves the saga-cascade test green). It only absorbed the self-inflicted concurrent dispatch, and in doing so made the harness seam diverge from the non-reentrant production `OnNextAsync`. Both seams now share one non-reentrant contract.
- The `InconsistentStateException` retry loop is removed. That exception fired only when two writes raced one grain's state — a direct symptom of the concurrent delivery. With serial delivery it no longer arises (verified across repeated full-suite runs), so a genuine fault now surfaces honestly at `Drain` rather than being swept under a tight `Task.Delay` retry.

The trade-off accepted is that the harness is slightly more constrained — it can no longer be "simpler" by firing deliveries concurrently. Fidelity wins, because a test framework whose delivery model differs from production gives false confidence, which is the precise failure mode this investigation chased for weeks.

Three guards keep it from regressing, one per failure vector:

- **`EventConsumerReentrancyParityTests`** (structural, no silo boot) fails the moment the harness delivery seam is marked interleaving again or otherwise diverges from `OnNextAsync`'s reentrancy.
- **`DeliverySerializationTests`** (in-process harness) fails if `InProcPublishExecutor` reverts to concurrent dispatch: it bursts one aggregate's events at a single projection and asserts the handler-turn high-water mark is one.
- **`ProjectionDeliverySerializationScenarios`** (streaming conformance, bound on Azure Queue Streams and Kafka, enforced by the binding-completeness guard) proves the serial-delivery-plus-exact-count property on every shipped real stream provider, so an Orleans upgrade that changed pulling-agent behaviour would fail here rather than in production.

This builds on ADR-0002, whose interleaved-delivery amendment it corrects, and on ADR-0058, whose read-your-writes cache the manufactured concurrency exposed. It supersedes nothing.
