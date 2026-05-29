# Substrate observability

Edict ships its own `Meter` named `"Edict"` for framework-level concerns — outbox, dead-letter, sagas, claim-check, handler latency. The substrate underneath surfaces its own metrics on its own `Meter`. An operator running Edict in production scrapes both: Edict's `"Edict"` meter for *what the framework decided*, and the substrate's meter for *what the queue/database/stream broker is actually doing*.

This page maps the substrate `Meter` names you wire into your OTel `MeterProviderBuilder.AddMeter(...)` alongside `EdictDiagnostics.SourceName`, and gives one line on what each tells you. [`alerts.md`](alerts.md) triage steps reference these by name; if a recipe says "check the substrate's connection-pool gauge," this is the page that names it.

## How to wire substrate meters

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

[`alerts.md`](alerts.md) recipes treat the framework metric as the **symptom** and the substrate metric as the **suspect**. "Stream falling behind" fires on `edict.event.handle.lag`; the triage line points you at `messaging.kafka.consumer.lag` (Kafka) or `azure.queue.request.duration` (Azure) to confirm the substrate is where the latency is being injected, not the consumer's `Handle` body.
