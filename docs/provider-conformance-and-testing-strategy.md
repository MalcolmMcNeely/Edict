# Provider Conformance & Testing Strategy

**Status:** Theorycraft / prompt for future working sessions. Not a decision, not an ADR.
Do **not** write ADRs from this — it is input to a discussion. Future sessions decide
ADRs per repo discipline (extends ADR 0016 *provider-scoped test layering*).

**Preconditions (assume done before the Kafka work starts):**

- Sagas shipped (ADR 0020).
- Claim-check shipped for large events on the Azure Queue Storage stream provider (large
  payload spills to blob; queue message carries a pointer), driven by the AQS ~64 KB
  message cap (~48 KB effective after base64).

---

## 1. Goal

Two intertwined goals:

1. **Add Kafka as a second stream-transport provider** (then Postgres on the storage
   seam), making "persistence-agnostic / pluggable transport" *demonstrated*, not asserted.
2. **Build a provider-agnostic conformance harness** so that `Edict.Azure`,
   future `Edict.Kafka` and `Edict.Postgres` all run the *same* high-value integration
   scenarios — including crash and load scenarios — proving the framework end-to-end on
   every backend, not in isolated unit slices.

---

## 2. Testing philosophy (this is the load-bearing part)

The point of the tests is **confidence that a change in one part of the system breaks a
test before it breaks production**. That dictates the shape:

- **Heavily prefer integration tests over isolated unit tests.** A pile of small unit
  tests that mock the seam is weak: it proves the parts in isolation and proves nothing
  about their composition — exactly the failure we care about. Unit tests are justified
  *only* for genuinely pure logic where an integration test would be slower and no more
  revealing: pure `OutboxSlice` state transitions, the dedup-ring data structure's bounded
  semantics, command-route resolution. Everything else earns its keep as an integration
  test through the real seam.
- **One scenario, run against every provider.** Scenarios are written **once** against
  abstract seam contracts and parameterised over a provider fixture. Adding `Edict.Kafka`
  must not mean rewriting the scenario battery — it means supplying a new fixture and
  watching the existing battery run (and fail honestly) against it.
- **Assert invariants, not steps.** Brittle step-by-step assertions rot. Assert the
  properties that must always hold — *no lost events, exactly-once effect, ordering per
  aggregate, bounded dedup memory* — plus a Verify snapshot of the timeline shape. This
  makes a cross-cutting regression surface as a failing invariant, wherever it originates.
- **Crash and load scenarios are first-class, not an afterthought**, and are
  **per-provider** because Azure Queue, Kafka and Postgres fail differently (offsets vs
  visibility timeouts vs transactions). A crash test that passes on Azurite proves nothing
  about Kafka rebalancing.

This *extends* ADR 0016, it does not replace the layers:

- `Edict.Core.Tests` — still the fast in-memory inner loop (mechanism logic, pure units).
- The conformance harness runs **in-memory as a smoke subset** for speed, and **full
  against Testcontainers** in each provider's suite.
- `Edict.Testing` (the *shipped*, consumer-facing in-memory framework) shares scenario
  DNA but is a different audience — do not conflate internal conformance infra with the
  shipped product.

---

## 3. The conformance harness

A shared test-support library (working name `Edict.Conformance`) containing:

- **`IProviderFixture`** — abstracts "stand up a working Edict cluster on backend X":
  the stream provider, the state store, the projection repository, lifecycle, and the
  fault-injection hooks below. One implementation per backend
  (`AzureProviderFixture`, `KafkaProviderFixture`, `PostgresProviderFixture`,
  `InMemoryProviderFixture`).
- **Scenario suites** — abstract xUnit classes each provider test project subclasses,
  binding its fixture. The provider projects (`Edict.Azure.Tests`, future
  `Edict.Kafka.Tests`, `Edict.Postgres.Tests`) contain almost no bespoke tests — they
  inherit the battery.
- **A workload driver** — fans out *N aggregates × M events*, then asserts the
  invariants, reused by both functional and load runs.

### 3a. Functional scenario battery (run on every provider)

| Scenario | Proves | Key invariant |
|---|---|---|
| Command → Event → Projection end-to-end | The seams compose at all | Projection reflects event exactly once |
| At-least-once redelivery + dedup | ADR-0002 realism | Duplicate delivery → single effect |
| Idempotency commit ordering | Dedup ring committed *after* `HandleAsync` | Crash before commit → safe redelivery |
| Per-aggregate ordering | Stream address `(eventType, sourceAggregateGuid)` | Events per aggregate observed in order |
| Outbox drain + PublishEvent effect | ADR 0018 engine | Every staged event eventually published once |
| Saga lifecycle | ADR 0020 correlation + reminders | Saga completes / times out deterministically |
| Dead-letter + block-intake + redrive | ADR 0019 | Poison msg → DLQ after max attempts; redrive replays |
| Claim-check large payload | Per-provider threshold | Large event round-trips intact |
| Trace context across the hop | ADR 0003 | Trace/span IDs survive real transport (assertions stay in `Edict.Telemetry.Tests`) |

### 3b. Crash / fault-injection scenarios (per-provider — failure modes differ)

Injection mechanisms: Orleans `TestCluster` silo kill/restart; Testcontainers
pause / stop / restart; network fault proxy (e.g. Toxiproxy) for latency, partition,
and packet loss; provider-specific (Kafka: broker kill + consumer-group rebalance;
Azure: throttling / visibility-timeout expiry; Postgres: connection drop, deadlock).

| Crash scenario | Expectation |
|---|---|
| Silo dies mid-`HandleAsync`, before dedup commit | Redelivery → effect applied exactly once (no double projection) |
| Crash between outbox write and publish | Outbox redrive recovers; event published once |
| Crash mid-saga | Saga resumes from durable state, no orphaned correlation |
| Transport unavailable then recovers | Jittered backoff retries (ADR 0019), no message loss |
| Poison message | Dead-lettered after max attempts; intake unblocked on redrive |
| State store transient failure on dedup commit | Defined behaviour — fail closed, redeliver, no silent dup |
| Kafka consumer-group rebalance under load | No lost or reordered events within a partition/aggregate |

### 3c. Load / soak (separate confidence gate, per-provider)

Not CI-blocking — a nightly / on-demand suite. Targets:

- **Throughput** at a defined concurrency, recorded per provider as an SLO trend.
- **Soak** (sustained run) catching resource leaks and, critically, proving the dedup
  ring stays **bounded** under pressure (ADR-0002) — unbounded growth is a silent killer.
- **Backpressure / hot-partition** behaviour (Kafka partition skew when many events map
  to one aggregate; Azure queue depth growth).
- Invariants from §3a must still hold *under load and concurrency*, not just at rest.

---

## 4. Seam model (get this precise first)

"Two seams" is a simplification. ~3–4 with different constraints:

1. **Stream transport** — Azure Queue Storage today; Kafka next.
2. **Dedup-ring + outbox + saga state** (`edict-state` grain storage) — load-bearing
   correctness store (ADR-0002). Hard constraint: read-your-writes consistency.
3. **Table-projection repository** (`IEdictTableRepository`) — read model, looser.
4. **PubSub store** — only if the chosen stream provider needs it. AQS runs
   implicit-subscription-only (which Edict is) without one; other providers may differ.

**Honest framing:** mostly orthogonal by construction, with **one latent coupling**
(stream provider may imply a PubSub-storage dependency) and **one correctness
constraint** (seam 2). Do not claim free N×M.

---

## 5. Kafka specifics

**Provider:** No first-party Orleans Kafka stream provider. Realistic option is the
community [`OrleansContrib/Orleans.Streams.Kafka`](https://github.com/OrleansContrib/Orleans.Streams.Kafka)
(Confluent SDK; can merge externally-produced messages into Orleans streams). Its health
on current Orleans / .NET 10 is the load-bearing unknown.

**Spike first (timeboxed):** `Orleans.Streams.Kafka` against an Aspire Kafka container
with a trivial implicit-subscription grain. Acceptance: round-trips an event on .NET 10
+ the repo's Orleans version, implicit subscription resolves, no PubSubStore required.
If it fails / is stale → fall back to Postgres-on-storage-seam, no time lost. Commit to
full integration only after the spike is green **and** it passes the §3 battery.

**Aspire:** First-party `Aspire.Hosting.Kafka` spins a `confluent-local` container;
`.WithKafkaUI()` adds a UI container. Demo parity with Azurite.

**Claim-check in Kafka — not needed at the AQS threshold; keep the mechanism,
make the threshold provider-configurable:**

- AQS needs it for a hard ~64 KB cap. Kafka `max.message.bytes` defaults ~1 MB,
  broker/topic-configurable far higher → trigger effectively "very high / off by default".
- Keep the mechanism pluggable: Kafka best practice still discourages multi-MB messages
  (broker memory, replication lag, latency), so claim-check stays a useful opt-in escape
  hatch.
- Threshold belongs to the stream-provider seam config, defaulted per provider (AQS
  ~48 KB; Kafka high/disabled). Fits the configurable-with-defaults pattern of
  `EdictOutboxOptions`.

---

## 6. Provider landscape & Aspire demonstrability

A reviewer must `aspire run` and see it work — no cloud account. Legend: ✅ first-party
Aspire container · 🧪 Aspire-managed emulator container · ⚠️ community/LocalStack only ·
❌ cloud-only · N/A in-process.

### Stream transport candidates

| Backend | Orleans provider | Aspire local demo | Notes |
|---|---|---|---|
| Azure Queue Storage | First-party | 🧪 Azurite | Current. Reference stack. |
| Kafka | Community (`Orleans.Streams.Kafka`) | ✅ `Aspire.Hosting.Kafka` + UI | Next target. Provider maturity is the risk. |
| Azure Event Hubs | First-party | 🧪 Event Hubs emulator | Kafka-protocol-compatible; safest first-party fallback. |
| Redis streams | First-party-ish | ✅ `Aspire.Hosting.Redis` | Cheap demo; weak "streaming" narrative. |
| NATS | Community (`OrleansContrib`) | ✅ `Aspire.Hosting.Nats` | Lightweight; niche in .NET contracting. |
| RabbitMQ | Community (less mature) | ✅ `Aspire.Hosting.RabbitMQ` | Ubiquitous; provider maturity uncertain. |
| AWS SQS | First-party | ⚠️ LocalStack | Demoable but not first-party local. |
| In-memory | First-party | N/A in-process | Test Framework / fast loop only. |

### Persistence candidates (storage seam)

| Backend | Orleans grain storage | Aspire local demo | Notes |
|---|---|---|---|
| Azure Table Storage | First-party | 🧪 Azurite | Current. Reference stack. |
| Azure Blob Storage | First-party | 🧪 Azurite | Same emulator; alternate state store. |
| PostgreSQL (ADO.NET) | First-party | ✅ `Aspire.Hosting.PostgreSQL` | Top contracting demand; strong next storage target. |
| SQL Server (ADO.NET) | First-party | ✅ `Aspire.Hosting.SqlServer` | Enterprise default; least differentiating. |
| MySQL (ADO.NET) | First-party | ✅ `Aspire.Hosting.MySql` | Low marginal value over Postgres. |
| Azure Cosmos DB | First-party | 🧪 Cosmos emulator | "Cloud-native NoSQL" signal; heavy emulator. |
| Redis | First-party | ✅ `Aspire.Hosting.Redis` | Fast; weaker durability story for dedup ring. |
| MongoDB | Community (`Orleans.Providers.MongoDB`) | ✅ `Aspire.Hosting.MongoDB` | Popular; community provider risk. |
| AWS DynamoDB | First-party | ⚠️ LocalStack | AWS-shop signal; not first-party local. |
| In-memory | First-party | N/A in-process | Test Framework / fast loop only. |

**Read:** anything ✅/🧪 clears the Aspire-demoable bar. Strongest narrative picks remain
**Kafka** (stream) and **PostgreSQL** (storage); **Azure Event Hubs** is the low-risk
first-party fallback if the Kafka provider spike fails.

---

## 7. Composition / config shape

Independent, defaulted registration calls — composition, not a config matrix:

```
builder.AddEdict()
       .UseKafkaStreams(...)        // or .UseAzureQueueStreams(...)
       .UsePostgresStorage(...);    // or .UseAzureTableStorage(...)
```

Defaults so the simple path needs neither call; each seam swappable in isolation;
claim-check threshold lives on the streams call.

---

## 8. Conformance gating (how the N×M trap is avoided)

Independent composition is an N×M space — do **not** Testcontainers-test every pairing.
The harness runs **per-seam, plus one reference cross**, not the cartesian product:

- Every scenario in §3 runs **per stream provider against the reference storage**, and
  **per storage provider against the reference stream** — a "plus", not a "cross".
- One **reference full-stack combo** (Azure Queues + Azure Tables) keeps the complete
  battery as the integrated baseline.
- A new provider is **"done" only when it passes the full §3a + §3b battery**; §3c load
  is a separate confidence gate, tracked as an SLO trend, not a pass/fail blocker.
- This rationale must exist before the matrix tempts N×M tests.

---

## 9. Open questions for the session

1. `Orleans.Streams.Kafka` viable on current Orleans / .NET 10, or adapter/fork needed?
2. Does the Kafka provider need a PubSub store under implicit-only subscriptions?
   (Confirms the seam-4 coupling.)
3. Kafka topic/partition strategy vs stream address
   `(eventType, sourceAggregateGuid)` — partition-key mapping, per-aggregate ordering.
4. Kafka at-least-once + consumer-group offsets vs the ADR-0002 post-`HandleAsync`
   dedup commit — do they compose without dup or loss?
5. Claim-check default threshold per provider; where the large-payload blob store lives
   when transport is Kafka.
6. Fault-injection toolkit choice (Toxiproxy vs container pause vs provider-native) — one
   abstraction in `IProviderFixture`, or per-provider implementations?
7. Does the conformance harness live in its own test-support project, and how does it
   share scenario DNA with the shipped `Edict.Testing` without coupling the two?

---

## 10. Out of scope

ADRs (decided in-session), Postgres storage-seam build (fast-follow after Kafka),
changing the dedup/outbox/saga mechanisms themselves.
