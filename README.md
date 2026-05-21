# Edict

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
    public Task Handle(OrderPlacedEvent evt) =>
        email.SendConfirmation(evt.OrderId, idempotencyKey: evt.EventId);
}
```

That's both sides of an event-driven flow. No Orleans interfaces, no stream wiring, no idempotency code, no serialization attributes, no DI registration. The framework wires `Handle` into the stream by method signature; at-least-once redeliveries are deduplicated by `EventId` in the base class.

## Why I built this

Distributed systems force a tax on every application that adopts them: idempotency, concurrency, atomicity across stores, trace continuity. Edict pays that tax once, in the base classes, so domain code stays about the domain.

Orleans is the foundation — the actor model, single-threaded grain activations, and in-memory cache fit distributed state naturally, without locks or two-phase commits.

The consumer-facing surface is six concepts: **Command Handler**, **Event Handler**, **Saga**, **Projection Builder**, **Sender**, **Stream**. Everything else is the framework's problem. That matters for AI-assisted development too: a small, well-defined pattern set is easier to compose against than asking an AI to invent a distributed system from scratch every time.

Edict isn't a production framework yet — there are gaps a hardened one would close. But the bet holds: a single programming model is worth more than a polyglot stack pretends, once the framework absorbs the hard parts.

## Tech stack

- C# / .NET 10
- Microsoft Orleans (grains, implicit stream subscriptions)
- Azure Queue Storage stream provider (Azurite locally)
- Azure Table Storage + Blob Storage for grain state and projections
- OpenTelemetry
- Roslyn source generators and analyzers
- Aspire AppHost (sample app)
- xUnit, Verify, Testcontainers

## Highlights

- **Event-driven, not event-sourced.** No event store, no replay. Events are transient; grain state is snapshot-persisted by Orleans.
- **Atomic state + events.** Aggregate state changes and raised events commit together in a single grain write — no distributed transaction needed.
- **Effectively-once handling.** Per-consumer deduplication is built into the base classes; nothing to opt into, nothing to forget.
- **Retries that don't block.** Failing outbox entries back off independently — one slow or broken downstream doesn't stall the rest.
- **Oversized events handled transparently.** Large payloads spill to blob storage at the commit boundary; the wire format never carries more than a pointer.
- **One trace per business flow.** Trace context is propagated across async stream hops, so `Send` through to the terminal handler is a single OpenTelemetry trace.
- **Dead-letter as observability, not back-pressure.** Permanently failing effects land in a queryable projection; the aggregate keeps accepting commands.
- **Configurable with sensible defaults.** Every framework knob is an options property with a default and startup validation — change what you need, leave the rest.
- **In-memory test framework.** Snapshot-test commands, events, and projection/saga state without containers; the framework itself is tested against real Azurite via Testcontainers.

## Running locally

You'll need .NET 10 and Docker (for the Azurite emulator that Aspire spins up).

```bash
git clone https://github.com/MalcolmMcNeely/Edict.git
cd Edict
dotnet run --project Sample/Sample.AppHost
```

The Aspire dashboard prints a URL on startup; from there the Sample API and Silo are reachable, and the OpenTelemetry traces light up the full command-to-projection chain.

Run the test suites with `dotnet test Edict/Edict.slnx`. On Windows, enable long paths first: `git config core.longpaths true`.

## How this was built

Edict was built in a few days using an AI-assisted workflow loosely modelled on [Matt Pocock's skills](https://github.com/mattpocock/skills) — a set of Claude Code skills that drive a disciplined PRD-then-TDD loop instead of free-form prompting. Each feature starts as a PRD on the [issue tracker](https://github.com/MalcolmMcNeely/Edict/issues), gets broken into tracer-bullet vertical slices, and lands via the red-green-refactor TDD skill. The whole decision trail is visible there: PRDs, slice issues, and the conversations that shaped each one.

Domain language lives in [`CONTEXT.md`](CONTEXT.md). Every load-bearing decision is recorded in [`docs/adr/`](docs/adr/).
