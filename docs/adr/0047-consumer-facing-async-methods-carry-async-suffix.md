# Consumer-facing async methods carry the `Async` suffix

Every `Task`-returning method a consumer types — or sees in IntelliSense on a base they derive from — is named with the `Async` suffix. The four consumer-facing bases (`EdictCommandHandler`, `EdictEventHandler`, `EdictSaga`, `EdictProjectionBuilder` / `EdictTableProjectionBuilder`) discover `HandleAsync(TMessage)`. The DI-injected sender exposes `Task<EdictCommandResult> SendAsync(EdictCommand)`. The integration-test entry point exposes `Task<EdictCommandResult> SendAsync(EdictCommand)`. There are no exceptions on the suffix — ADR-0036 is the documented exception to TAP's *CancellationToken* half (`HandleAsync` carries the suffix but takes no token), not to the suffix itself.

The convention is enforced at build time. `EDICT018` (in `Edict.Analyzers/Handlers/HandleMethodNameAnalyzer.cs`) fires at Error severity on any `Task<...>`-returning method literally named `Handle` on a class deriving from one of the four consumer bases, with a Roslyn code-fix that performs the rename in place. The analyzer deliberately string-matches the old name — its job is to catch consumers who write the pre-rename name even after the generator's discovery constant has flipped to `"HandleAsync"`.

## Considered Options

- **Status quo: `Handle` + `Send`, no suffix.** Rejected. From the consumer's seat — a .NET dev with TAP muscle memory and IntelliSense — the surface looked inconsistent: `IEdictSender.Send` was the lone unsuffixed entry in `Edict.Contracts.Sending` while every neighbouring repo interface (`IEdictDeadLetterRepository`, `IEdictTableWriteStore`, `IEdictClaimCheckStore`, `ITableRepository`) already carried `Async`. The generator's bare `Handle` name had a silent-no-op failure mode — a method written as `HandleAsync` (or any name other than `Handle`) compiled cleanly and never fired at runtime, because the generator only emitted a dispatch arm for methods discovered by exact string match. The discovery tax landed on every new consumer, every time.

- **Suffix everywhere, no analyzer.** Rejected. The generator's discovery-by-name mechanism turns a typo into a silent runtime no-op. A build-time analyzer at Error severity is the cheapest way to turn that failure mode into something the consumer cannot miss. The analyzer is the load-bearing half of the convention; the rename alone would have replaced one silent trap with another.

- **`[Obsolete]` shim on `Send` / `Handle` for one release.** Rejected. Pre-release, no released consumers, no compat constraints — the atomic rename in PRD #235's Slice 2 was the cutover. A shim would have widened the consumer surface to two valid names for a release, doubling the documentation footprint and the IntelliSense noise for no consumer benefit.

## Why the consumer-perspective lens decided this

The maintainer-perspective rationale for the old `Handle` name was real but internal: "the framework is always-async, so the suffix is redundant", "the generator discovers by name and there's no overload to disambiguate", "Orleans's own reminder family takes a partial-suffix shape". Each one was true from inside the framework and irrelevant from the consumer's seat. The consumer sees `Task<EdictCommandResult>` on a method named `Handle` and reaches for the suffix; the maintainer's "but it's always async, so the suffix would be redundant" argument is invisible to them. Three of the four pro-`Handle` arguments raised during the PRD #235 grilling turned out to be maintainer-convenience dressed up as design principle. The fourth — that `HandleAsync` makes the generator's discovery-by-name silent-no-op slightly more likely (consumers might still mistype it as `Handle`) — is the one the analyzer exists to neutralise.

This is the lens recorded for the next maintainer: when defending an Edict surface decision, separate the maintainer-convenience arguments from the consumer-perspective arguments. The consumer lens is decisive on consumer-facing surface; the maintainer lens is decisive on internal mechanism.

## Consequences

- The generator discovers `HandleAsync` (`Edict.Generators.EdictWellKnownNames.HandleMethodName = "HandleAsync"`); the same constant is compile-linked into `Edict.Analyzers` and `Edict.Mcp` and the three-assembly parity test (`EdictWellKnownNamesParityTests`) guards drift.
- `EDICT018` fires at Error severity on any handler method literally named `Handle`. A consumer cannot ship a handler that compiles but silently never fires.
- ADR-0036 is the documented exception to TAP's CancellationToken half: `HandleAsync` honours the suffix, declines the token. The Orleans stream `IAsyncObserver.OnNextAsync` carries no CT, at-least-once delivery + per-consumer dedup (ADR-0002) + Outbox retry-and-dead-letter (ADR-0015/0018) is the cancellation/failure story.
- `Raise(EdictEvent)` on `EdictCommandHandler` and `Dispatch(EdictCommand)` on `EdictSaga` keep no suffix — they are deliberately synchronous (buffer onto a list, do not return `Task`). No suffix because not async, not because of a convention exception.
