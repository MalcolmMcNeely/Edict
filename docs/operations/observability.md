# Operating Edict telemetry

Edict ships its own `Meter` and `ActivitySource`, both named `"Edict"`, for framework-level concerns — outbox, dead-letter, sagas, claim-check, handler latency, and the trace causality graph. The substrate underneath surfaces its own metrics on its own `Meter`. An operator running Edict in production scrapes both: Edict's `"Edict"` meter for *what the framework decided*, and the substrate's meter for *what the queue/database/stream broker is actually doing*.

This page covers two things: how to pivot from an Edict metric to a representative trace (the exemplar→trace pivot, and which metrics deliberately carry no exemplar), how to sample Edict traces at scale, and then the substrate `Meter` map you wire into your OTel `MeterProviderBuilder.AddMeter(...)` alongside `EdictDiagnostics.SourceName`. [`alerts.md`](alerts.md) triage steps reference both by name; if a recipe says "pick a slow exemplar" or "check the substrate's connection-pool gauge," this is the page that explains it.

## Exemplar → trace pivot

Edict's trace model is one trace per grain turn, linked across turn boundaries ([ADR-0060](../adr/0060-trace-causality-at-scale-one-turn-links.md), supersedes ADR-0003; the model is described in [`telemetry.md`](../usage/concepts/telemetry.md)). Exemplars are what let an operator jump from a histogram bucket — "the slow tail of `command.handle.duration`" — to a concrete trace in that bucket. The wiring is a single line on the metrics builder:

```csharp
metrics.SetExemplarFilter(ExemplarFilterType.TraceBased);
```

With the trace-based filter active, any measurement recorded *while a recording Edict span is current* attaches that span's trace as an exemplar. From a slow bucket in Aspire / Grafana / Tempo you click the exemplar and land on the turn's trace; from there you follow the links to the cause (the publishing turn, the arming command, the failing entry).

Every per-operation Edict counter and histogram is recorded inside its turn's span precisely so the pivot works: `command.handle.duration` inside `edict.command.handle`, `event.handle.duration` / `event.handle.lag` inside `edict.event.handle`, `idempotency.duplicate.count` inside `edict.event.deduplicated`, `claim_check.payload.size` inside `edict.event.claim_check.put` when the event spilled (so the exemplar points at the blob-write trace), and `saga.timeout.fired` inside `edict.saga.timeout`.

### The three carve-outs

Three meters deliberately carry **no** exemplar. Their absence is a documented decision, not a wiring gap — do not chase a missing exemplar on these:

- **The observable gauges** (`outbox.pending.count`, `outbox.oldest_entry.age`, `saga.progress.age`). The gauge callback runs on the scrape thread, decoupled from any grain turn, so there is no operation in flight and no `Activity.Current` to sample — an exemplar is impossible by construction ([ADR-0040](../adr/0040-silo-local-metrics-cache-for-observable-gauges.md)).
- **`outbox.drain.*`** (`drain.count`, `drain.entries`). A per-pass aggregate that can cover entries staged by many different command turns and traces; a single-trace exemplar would misrepresent the batch, so the framework starts no span for the drain pass ([ADR-0038](../adr/0038-meters-naming-and-cross-cutting-attributes.md)).
- **`saga.completed`**. Counted only after the saga's terminal write is durable, so a write-fault redelivery cannot double-count it; that durable-commit point sits outside any span (the inline completion path has no handle span there at all). Forcing it under a span would regress exactly-once counting, so it stays the bare cardinality-bounded denominator companion to `saga.timeout.fired` ([ADR-0039](../adr/0039-metrics-cardinality-policy.md)). Pivot to the completing trace from the `edict.command.handle` / `edict.event.handle` spans and their links instead.

## Sampling at scale

Because a trace is one grain turn and turns are connected by `ActivityLink`s, each trace makes its **own** head-sampling decision. The link carries the producing turn's real sampled flag (restore honours the flag byte rather than forcing `Recorded`), so a dropped command yields a fully-dropped turn-trace and a sampled one yields a complete turn-trace — head sampling at `edict.command` is your volume lever.

The catch: head sampling alone can sample one turn *in* and its linked cause *out*, severing "follow the link to the cause." To keep a whole link-group together, run **tail sampling or a link-aware sampler** at the collector — a tail sampler sees all spans of a link-group before deciding, and a link-aware sampler propagates the decision across links. This is the deliberate trade of the per-turn model: bounded, tail-sampleable traces in exchange for a collector that understands links. Set head sampling for volume; set tail/link-aware sampling for causal completeness.

## Audit metrics and the drain span

When auditing is on, Edict captures every command decision and raised event to a durable WORM store (see [`audit-log.md`](../usage/concepts/audit-log.md)). The audit store is the **legal record**; these metrics are the operational pulse over it, and the trace is the sampled overlay. Alert on the counters, never on the presence of a span — a dropped trace says nothing about whether a record was written, but the counters and the store do.

The capture and the drain are separate moments, and so are their signals:

| Instrument | Type | What it tells you |
|---|---|---|
| `edict.audit.records.captured` | counter (tags: `edict.audit.kind` ∈ `command\|event`, `edict.audit.outcome` ∈ `accepted\|rejected` on commands) | The "we recorded this decision" signal, incremented as each C1 command record and E1 event record is staged. It is recorded inside the deciding command's `edict.command.handle` turn, so it carries an exemplar to the turn that made the decision. The `rejected` slice is the one a stream-based scheme never sees — a denied command raises no event yet is counted here. |
| `edict.audit.drain.failure` | counter (tag: `edict.grain.type`) | A batch of captured records that could not be durably written to the WORM store. This is a **compliance signal — the record exists in grain state but is not yet durable — not an effect-delivery retry**, so it is distinct from `outbox.drain.*`. The records stay staged (never dropped) and a reminder retries; **alert on any non-zero rate**. Recorded inside the `edict.audit.drain` span, so its exemplar points at the failing drain turn. |

The **`edict.audit.drain`** span is the drain turn itself: pending records being written to the store off the command's hot path, running as a one-shot grain timer, an activation drain, or a reminder retry. It is its own root (not a child of the capturing command) precisely because it runs after the turn that captured the record has committed and returned. On a store failure the span carries `ActivityStatusCode.Error`; pivot from a `drain.failure` exemplar to it to see which write failed.

These two counters are the audit exception to the carve-out rule above: both are recorded inside a live span, so both carry exemplars.

## Tenant isolation metric

When tenancy is on, the isolation filter is the runtime backstop that compares the tenant parsed from a target grain's own key against the calling turn's ambient tenant (see [`multi-tenancy.md`](../usage/concepts/multi-tenancy.md)). The common path is silent — every key is composed from the ambient tenant, so the two agree by construction. The filter only reaches the counter on a real divergence, so its volume is naturally low and every increment is meaningful.

| Instrument | Type | What it tells you |
|---|---|---|
| `edict.tenant.crossing.count` | counter (tag: `edict.tenant.crossing.outcome` ∈ `authorized\|denied`) | A tenant-boundary divergence the filter reached. `denied` is the breach signal — a call into another tenant's wall the filter refused and threw on; `authorized` is an explicitly-authorized crossing that proceeded. The tag is a closed allowlist; **the tenant value is never a meter tag** — an unbounded tenant id would explode the dimension, so it lives on the `edict.tenant.cross_denied` / `edict.tenant.cross_authorized` span events only. **Alert on `denied` greater than zero**: on the common path it is structurally unreachable, so any denial is either a keying bug that bypassed the composition chokepoint or an illegitimate reach into another wall. Pivot from the count to the span event to see which walls (`edict.tenant.relay` → `edict.tenant.key`) the call crossed. |

## Substrate meters

The substrate underneath Edict surfaces its own metrics on its own `Meter`. This is the map of those names; [`alerts.md`](alerts.md) treats the framework metric as the **symptom** and the substrate metric as the **suspect**. Wire each substrate `Meter` alongside `EdictDiagnostics.SourceName`:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(EdictDiagnostics.SourceName)   // framework — outbox, dead-letter, sagas, claim-check
        .AddMeter("Npgsql")                       // Postgres connection pool + command stats
        .AddMeter("Confluent.Kafka")              // Kafka client throughput + queue depth
        .AddMeter("Azure.Storage.Queues")         // Azure Queue Storage request latency + errors
        .AddPrometheusExporter());
```

The `MeterListener` example in `Edict.Benchmarks.Throughput.Tests/NpgsqlPoolListener.cs` shows the read-side shape if you want to assert on substrate metrics in a test rather than scrape them.

## Postgres — `Npgsql` meter

Edict.Postgres uses the Npgsql ADO.NET driver. Npgsql 9+ publishes pool and command instruments on the `"Npgsql"` meter. Names are **singular** (`db.client.connection.*`), not the OTel-spec plural — the Npgsql team shipped these before the spec stabilised; see the comments in `NpgsqlPoolListener.cs` for the full mismatch list.

| Instrument | Type | What it tells you |
|---|---|---|
| `db.client.connection.count` | observable up-down counter (tag: `db.client.connection.state=idle\|used`) | Current connections in the pool, by state. The "used" series approaching the cap is the pressure signal. |
| `db.client.connection.max` | observable up-down counter | Pool ceiling (`EdictPostgresOptions.MaxPoolSize`). Constant per data source. |
| `db.client.connection.npgsql.pending_requests` | up-down counter (delta) | Threads parked waiting for a connection. Non-zero means the pool is fully checked out. Sustained > 1s is the ADR-0029 saturation threshold. |
| `db.client.connection.npgsql.create_time` | histogram (seconds) | New-connection establishment cost. p99 trending up under load is the closest signal to the OTel-spec `wait_time` that Npgsql doesn't ship. |
| `db.client.commands.executing` | observable up-down counter | Commands currently in flight against the pool. Spikes correlate with grain-storage write bursts. |

Tag note: Npgsql 10 uses `db.client.connection.state` (the OTel-spec tag key) but ships singular instrument names — be careful when reading dashboards written for one and not the other.

### Postgres grain-storage transient retry

Edict.Postgres rolls its own grain storage (Orleans' shipped `AdoNetGrainStorage` collapses every `Grain<T>` sharing a `[EdictRouteKey]` into one row), so unlike the Azure pairing it gets no transient-fault resilience from its driver. The provider adds it explicitly: every storage seam retries a transient `NpgsqlException` before surfacing `EdictPostgresStorageException`. One counter on the framework `"Edict"` meter (not the `Npgsql` meter) reports the result.

| Instrument | Type | What it tells you |
|---|---|---|
| `edict.postgres.storage.retry.count` | counter (tags: `edict.postgres.storage.operation` ∈ `Read\|Write\|Clear`, `edict.postgres.storage.outcome` ∈ `recovered\|exhausted`) | Grain-storage transient retries that fired. The `recovered` slice means the substrate shed a transient fault that a later attempt cleared; a rising `recovered` rate is an early warning that the Postgres connection path is flapping. The `exhausted` slice means retries no longer cleared it and `EdictPostgresStorageException` surfaced — pair a rising `exhausted` rate with the `Npgsql` pool gauges above to find the saturated resource. Tune the budget with `EdictPostgresPersistenceOptions.StorageRetryCount` / `StorageRetryBaseDelay`. |

This counter carries no exemplar by design: it is recorded from the retry hook, decoupled from any command/event span.

## Kafka — `Confluent.Kafka` meter

Edict.Kafka uses the Confluent.Kafka .NET wrapper around librdkafka. The wrapper does **not** emit `System.Diagnostics.Metrics` instruments out of the box; you wire `OpenTelemetry.Instrumentation.ConfluentKafka` (or your own statistics callback) to surface them on the `"Confluent.Kafka"` meter.

| Instrument | Type | What it tells you |
|---|---|---|
| `messaging.kafka.client.consumed.messages` | counter | Messages successfully consumed. Stalling means consumers are not pulling — pair with `consumer.lag`. |
| `messaging.kafka.consumer.lag` | observable gauge (tag: `topic`, `partition`) | Per-partition consumer lag in messages. The substrate-native equivalent of `edict.event.handle.lag` — both should move together. |
| `messaging.kafka.client.produced.messages` | counter | Messages successfully produced. A drop pairs with the `edict.outbox.drain.count` curve if the publish executor is the bottleneck. |
| `messaging.kafka.producer.queue.size` | observable gauge | librdkafka's in-process produce queue. Climbing under steady load means broker-side back-pressure. |
| `messaging.kafka.broker.throttle.time` | histogram | Time the broker asked us to throttle. Non-zero means broker-side quotas are kicking in — usually a partition-imbalance or hot-key issue. |

Edict's `EdictKafkaStreamsOptions.PartitionCountByStream` directly drives the parallelism ceiling for `messaging.kafka.consumer.lag` per topic — under-partitioned topics show as a tall single-partition lag spike with the others empty.

## Azure Queue Storage — `Azure.Storage.Queues` meter

Edict.Azure rides Orleans' Azure Queue Storage stream provider, which uses the Azure SDK's `Azure.Storage.Queues` client. The Azure SDK ships an `ActivitySource` and a `Meter`; `OpenTelemetry.Instrumentation.AzureCore` enables both.

| Instrument | Type | What it tells you |
|---|---|---|
| `azure.queue.requests` | counter (tag: `operation=enqueue\|dequeue\|peek\|delete`, `status`) | Per-operation request volume + success/failure split. A sustained `status=failure` slice points at queue-side throttling or auth drift. |
| `azure.queue.request.duration` | histogram (tag: `operation`) | Per-operation latency. p99 trending up correlates 1:1 with `edict.event.handle.lag` if the stream provider is the bottleneck. |
| `azure.queue.message.dequeue.count` | counter | Total dequeues across all queues. Pairs with Orleans' own `Orleans.Streaming.PubSubStore.*` for stream-provider health. |
| Orleans `Orleans.Streaming.Queue.*` | various | The Orleans stream provider's own surface — `read.errors`, `read.failures`, `messages.read`. Orleans 10 emits these on the `"Microsoft.Orleans.Streaming"` meter; add it to your provider builder. |

## Reading the docs together

[`alerts.md`](alerts.md) recipes treat the framework metric as the **symptom** and the substrate metric as the **suspect**. "Stream falling behind" fires on `edict.event.handle.lag`; the triage line points you at `messaging.kafka.consumer.lag` (Kafka) or `azure.queue.request.duration` (Azure) to confirm the substrate is where the latency is being injected, not the consumer's `HandleAsync` body.
