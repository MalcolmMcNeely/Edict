# Edict

[![CI](https://github.com/MalcolmMcNeely/Edict/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/MalcolmMcNeely/Edict/actions/workflows/ci.yml)

New here? Start with [`docs/usage/getting-started.md`](docs/usage/getting-started.md).

A CQRS, event-driven framework for Microsoft Orleans. You write the handler; Edict handles the wire format, the idempotency, the trace continuity, the outbox, the retries, and the dead-letter forensics.

```csharp
public partial class OrderCommandHandler : EdictCommandHandler<OrderState>
{
    public Task<EdictCommandResult> Handle(PlaceOrderCommand cmd)
    {
        State.Status = OrderStatus.Open;
        Raise(new OrderPlacedEvent(cmd.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
```

Subscribing to that event is just as small:

```csharp
public sealed partial class OrderEmailHandler(IEmailSender email) : EdictEventHandler
{
    public Task Handle(OrderPlacedEvent evt) => email.SendConfirmation(evt.OrderId, evt.EventId);
}
```

That's both sides of an event-driven flow. No Orleans interfaces, no stream wiring, no idempotency code, no serialization attributes, no DI registration. The framework wires `Handle` into the stream by method signature; at-least-once redeliveries are deduplicated by `EventId` in the base class.

The same handler code runs on either of two reference substrate pairings — Azure Storage, or Kafka + Postgres — both passing the same conformance battery. Substrate-pluggability is demonstrated, not claimed.

## Testing and chaos

Edict ships with an in-memory test framework so command, event, saga, and projection handlers can be exercised without spinning up Orleans, Azurite, or any container. Tests `Send` a command, `Drain` the cascade, and inspect saga progress or projection rows directly.

```csharp
await using var app = await EdictTestApp.StartAsync(b => b
    .WithConsumer(typeof(OrderCommandHandler).Assembly));

await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
await app.Send(new SubmitOrderCommand(orderId, Amount: 100m));
await app.Drain();

var progress = await app.GetSagaProgress<OrderPaymentSaga, OrderPaymentProgress>(orderId);

await Verify(progress);
```

Three commands flow through a command handler, a saga, and a projection builder — all in-process, no containers — and one Verify snapshot captures the entire outcome.

Chaos is on by default: the in-memory executor models at-least-once delivery — duplicate redelivery and bounded reorder, seeded for determinism — so every test exercises the dedup ring and reorder-tolerance guarantees the production substrate requires. The framework itself is tested against real Azurite via Testcontainers, so the in-memory seam stays honest.

## Why Orleans?

Two pods. Same order. Two writes at once. The conventional answer is a distributed lock — and then a cache-invalidation channel, and then session affinity at the load balancer, and then giving up on in-memory state.

Orleans's answer is one rule: each entity has a single in-memory home — one node, one activation, one thread at a time.

From that one rule:

- **The distributed lock disappears.** Concurrent calls to the same entity queue on the activation; no second writer exists.
- **Cache invalidation disappears.** The activation is the cache. There is no second copy to invalidate.
- **Session affinity disappears.** The runtime routes by entity identity, not by load-balancer configuration.
- **In-memory state stops being a code smell.** Local fields outlive a request because the activation does.

Orleans dissolves the infrastructure tax. It does not dissolve the application-layer tax — idempotency for duplicate deliveries, atomicity between state and events, trace continuity across async hops, forensics for poison messages. That's where Edict comes in.

## Why Edict?

A webhook fires twice. A handler crashes after writing state but before publishing the event. A trace from `Send` ends at the first queue hop. A poison message blocks the aggregate. Conventional .NET answers each one with a different library and a fresh row in a fresh table.

Edict's answer is one rule: every consumer inherits a base class that wraps your `Handle` in an envelope carrying a dedup key, the trace context, and the outbox commit.

From that one wrapping:

- **Idempotency is automatic.** The base class deduplicates by `EventId` before invoking `Handle`. Nothing to opt into.
- **State and events commit together.** A single grain write covers aggregate state and outbox entries; no two-phase commit.
- **One trace per business flow.** The envelope carries trace context across every async stream hop, so `Send` through to the terminal handler is one OpenTelemetry trace.
- **Poison messages land in a queryable dead-letter projection.** The aggregate keeps accepting commands; the failure has a forensic home.

The consumer-facing surface is six concepts: **Command Handler**, **Event Handler**, **Saga**, **Projection Builder**, **Sender**, **Stream**. Everything else is the framework's problem. That matters for AI-assisted development too: a small, well-defined pattern set is easier to compose against than asking an AI to invent a distributed system from scratch every time.

Edict isn't a production framework yet — there are gaps a hardened one would close. But the bet holds: a single programming model is worth more than a polyglot stack pretends, once the framework absorbs the hard parts.

## Benchmarks

`Edict.Benchmarks.Throughput` sweeps issuer parallelism against any registered substrate (`azure`, `kafkapostgres`, or `all`) and writes results to [`docs/benchmarks/`](docs/benchmarks/).

- [`throughput.md`](docs/benchmarks/throughput.md) — measured per-event latency and sustained EPS on both substrates, framed as a regression guard on a known substrate, not a sizing tool.
- [`production-scale-estimate.md`](docs/benchmarks/production-scale-estimate.md) — back-of-envelope extrapolation to real Azure Storage and managed Kafka + Postgres at 1/2/4/8 silos, with substrate ceilings and the assumptions worth pressure-testing.

## Tech stack

C# / .NET 10, Microsoft Orleans, OpenTelemetry, Roslyn source generators + analyzers, .NET Aspire, xUnit + Verify + Testcontainers.

**Technology plugins** — same domain code, one conformance battery:

| Pairing          | Streaming    | State + projections |
| ---------------- | ------------ | ------------------- |
| Azure Storage    | Azure Queue  | Azure Table + Blob  |
| Kafka + Postgres | Apache Kafka | PostgreSQL          |

## Highlights

- **Pluggable.** Same handlers on Azure Storage or Kafka + Postgres.
- **Event-driven, not event-sourced.** Events are transient; grain state is snapshot-persisted by Orleans.
- **Atomic state + events.** One grain write covers both.
- **Effectively-once.** Per-consumer dedup in the base class.
- **Retries that don't block.** Failing outbox entries back off independently.
- **Claim check.** Large payloads spill to blob storage; the wire format carries a pointer.
- **One trace per business flow.** Trace context propagated across every async stream hop.
- **Operational metrics.** Outbox depth + oldest-entry age, dead-letter rate by failure kind, handler p99 by command/event type, stream lag, saga progress age, claim-check size distribution, drain-cycle stability — all on a single `Meter` named `"Edict"`. Vendor-neutral PromQL alert recipes in [`docs/operations/alerts.md`](docs/operations/alerts.md).
- **Dead-letter as observability.** Permanently failing effects land in a queryable projection.
- **Configurable.** Every knob is an options property with a default and startup validation.
- **In-memory tests.** Send → drain → verify without containers; the framework itself is tested against real Azurite via Testcontainers.

## What's next

- **Saga timeouts.** Declarative deadlines per saga step with automatic compensation — today a saga that never gets its next event sits forever.
- **Sharded dead-letter projection.** Today it's a single grain — under a poison-event storm the *thing recording the storm* becomes the bottleneck.
- **Outbox circuit breaker.** Per-target breaker on the executor seam, so a flapping downstream stops getting hammered by per-entry retries.
- **Rate-limiter and monotonic-sequence primitives.** Token bucket as a grain base; per-aggregate gap-free `Seq` on events for audit consumers.
- **More substrates.** AWS SQS + DynamoDB. NATS JetStream. Cosmos DB. MongoDB. The conformance harness already exists, so the next substrate add is mostly a queue adapter and a state-storage provider — no framework changes needed.

## Running locally

You'll need .NET 10 and Docker.

```bash
git clone https://github.com/MalcolmMcNeely/Edict.git
cd Edict
dotnet run --project Sample/Sample.Azure.AppHost
```

The Aspire dashboard prints a URL on startup. From there, follow two links:

- **Sample.Azure.Web** — the demo at `/`. A paused dashboard of a live order-processing system. Press ▶ to start traffic, or press **Fire one order** for a single deterministic lifecycle that produces one clean trace tree in Aspire. Click any row in the orders table to spotlight it; the right-hand timeline shows that order's state transitions with the span name beside each row, so you can navigate the Aspire trace by reading down the spotlight. Three injection buttons demonstrate the failure modes — poison, oversize-payload (claim check), and saga-rejected commands.
- **Aspire telemetry** — the trace view is the source of truth for what Edict is actually doing. Look for spans named `edict.command.send`, `edict.event.publish`, and `edict.event.handle`. Oversize events carry `envelope.shape=ClaimCheck` on the publish span.

Two spokes hang off the demo: `/dead-letter` lists outbox effects that exhausted their retry budget; `/metrics` shows live tiles for outbox depth, dead-letter rate, handler p99 and stream lag, each with its PromQL recipe inline.

Run the test suites with `dotnet test Edict/Edict.slnx`. On Windows, enable long paths first: `git config core.longpaths true`.

### Running on Kafka + Postgres

The same sample domain runs on Kafka and PostgreSQL — same handlers, same conformance scenarios, different substrate.

```bash
dotnet run --project Sample/Sample.KafkaPostgres.AppHost
```

Aspire brings up Kafka, Postgres, the silo, and the web tier. Kafka UI and pgAdmin sidecars are wired in for topic and table inspection.

## Agentic tooling (dogfood)

This repo dogfoods two `dotnet tool`s pinned in `.config/dotnet-tools.json`: [`Edict.Mcp`](Edict/Edict.Mcp/README.md) (Model Context Protocol server, CLI `edict-mcp`) and [`Edict.ClaudeSkills`](Edict/Edict.ClaudeSkills/README.md) (Claude Code skill installer, CLI `edict-skills`). `.mcp.json` at the repo root wires the MCP server into any Claude Code session opened here, and the five `edict-*` consumer skills sit in `.claude/skills/` alongside the framework-dev ones. Manifest-pinned install is the recommended path for consumers too — what's lived in here is what the READMEs document.

Until the lockstep release pushes the two tools to nuget.org, pack them locally first; then restore from the bundled feed:

```bash
dotnet pack Edict/Edict.Mcp           -c Release -o artifacts-dryrun -p:MinVerVersionOverride=0.1.1-preview
dotnet pack Edict/Edict.ClaudeSkills  -c Release -o artifacts-dryrun -p:MinVerVersionOverride=0.1.1-preview
dotnet tool restore
```

`nuget.config` adds `./artifacts-dryrun/` as a NuGet source so the restore finds the freshly-packed nupkgs. After restore, `dotnet edict-mcp` is what `.mcp.json` invokes, and `dotnet edict-skills install` is what populates `.claude/skills/edict-*` — the same path a consumer follows.

## How this was built

Edict was/is built using an AI-assisted workflow loosely modelled on [Matt Pocock's skills](https://github.com/mattpocock/skills) — a set of Claude Code skills that drive a disciplined PRD-then-TDD loop instead of free-form prompting. Each feature starts as a PRD on the [issue tracker](https://github.com/MalcolmMcNeely/Edict/issues), gets broken into tracer-bullet vertical slices, and lands via the red-green-refactor TDD skill. The whole decision trail is visible there: PRDs, slice issues, and the conversations that shaped each one.

Domain language lives in [`CONTEXT.md`](CONTEXT.md). Every load-bearing decision is recorded in [`docs/adr/`](docs/adr/).
