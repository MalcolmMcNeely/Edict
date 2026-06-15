---
name: edict-silo-wiring
description: Use this skill when working on a consumer app built on Edict and editing Program.cs or any silo wiring file — anywhere the AddEdict* extension chain is being assembled or changed. Covers the per-substrate AddEdict* matrix.
---

# Wiring an Edict silo

An Edict silo wires one streaming provider plus one persistence provider, plus optional claim-check and other framework opt-ins. The four supported pairings are the ones the conformance battery proves green; mix-and-match outside that matrix is unsupported.

## Always inspect current wiring before suggesting changes

Before you propose adding or removing an `AddEdict*` call, invoke **`edict_describe_silo_wiring`**. It locates `Program.cs` in the loaded solution, walks the `ISiloBuilder` invocation chain, and reports both:

- **Wired** — the `AddEdict*` extensions already in the silo builder.
- **Missing** — known `AddEdict*` extensions the consumer might want next. The classic example: a consumer asks for a Claim Check setup but `AddEdictAzureBlobClaimCheck` is not wired — `edict_describe_silo_wiring` surfaces this before you suggest the wrong fix.

This is the load-bearing trigger for this skill: call `edict_describe_silo_wiring` before any wiring change. Guessing the silo's substrate from grep or naming hints is exactly the failure mode the tool exists to prevent.

## Check the configuration after wiring

Once the `AddEdict*` chain is in place, run **`edict_check_configuration`** before you call the silo done. It reads `Program.cs`, works out which option knobs the consumer has set inside each `AddEdict*` call, and returns a best-effort verdict of required-but-unset knobs:

- An empty Kafka `BootstrapServers` (required, no default) or an unset Postgres `ConnectionString` (required, no default) is reported as an error: the host will not start until it is set.
- The Azure streaming case is a soft reminder to confirm a `QueueServiceClient` is set on the options or registered in DI (for example via `AddAzureClients`), so the tool does not falsely fail a DI-registered client.

A brand-new project is the degenerate case where almost everything is missing, so the same check that diagnoses an established silo walks a fresh one off the ground. The tool is best-effort and resolves only set-versus-not-set: `EdictWiringValidator`, which runs at host start with live DI, is ground truth.

## The AddEdict* matrix

| Extension | Assembly | Purpose |
| --- | --- | --- |
| `AddEdict()` | `Edict.Core` | The required core registration: handler discovery, outbox, telemetry. Every silo and every client needs this. |
| `AddEdictOutbox()` | `Edict.Core` | The outbox host wiring. Required on silos that host Command Handlers (and the framework attaches it on the bases that need it). |
| `AddEdictAzureStreams(...)` | `Edict.Azure.Streaming` | Azure Queue Storage stream provider. |
| `AddEdictAzureBlobClaimCheck(...)` | `Edict.Azure.Streaming` | Azure Blob claim-check store for oversized events. Optional but almost always wanted on the Azure streaming pairing. |
| `AddEdictAzurePersistence(...)` | `Edict.Azure.Persistence` | Azure Table Storage as the grain-state provider. |
| `AddEdictPostgresPersistence(...)` | `Edict.Postgres` | PostgreSQL as the grain-state provider. |
| `AddEdictKafkaStreams(...)` | `Edict.Kafka` | Kafka as the stream provider. |
| `WithAudit()` | `Edict.Core` | Turns on audit capture for the silo. Pairs with `silo.Services.AddEdictAudit(resolver)` (the origin principal resolver). Optional; see the auditing section. |

Each `AddEdict*` extension that takes a `(...)` argument accepts an `Action<T>` over its options class. The canonical reference for every options property, its default, and its validation rule is the `docs/configuration` folder in the Edict repository — `core.md` for the provider-agnostic knobs, plus the page matching your streaming and persistence choice. Reach for it before hand-tuning a literal in `Program.cs`.

Command Validators (`EdictCommandValidator<TCommand>` subclasses) are auto-discovered by `AddEdict(assembly)` through FluentValidation's `AddValidatorsFromAssemblies`. No manual DI registration is needed; adding a validator class to the consumer assembly is enough.

## Schedule default timeout

`AddEdict(...)` takes three optional configuration lambdas: core `EdictOptions`, then `EdictSagaOptions`, then `EdictCommandHandlerScheduleOptions`. The third configures the default cap for Command Handler schedules:

```csharp
silo.AddEdict(
    core => { core.OutboxMaxAttempts = 3; },
    saga => { saga.DefaultTimeout = TimeSpan.FromDays(7); },
    schedule => { schedule.DefaultTimeout = TimeSpan.FromDays(7); });
```

`EdictCommandHandlerScheduleOptions.DefaultTimeout` is the cap applied to any Command Handler schedule started without an explicit `timeout:`. It ships finite at 7 days so no schedule can tick forever by accident, and it is positive-or-null validated at host start (a zero or negative value aggregates into the boot-time `EdictWiringException`). Set it to `null` to return the silo to uncapped command-handler schedules. This default governs Command Handler schedules only: a Saga's schedule is bounded by that saga's own `[EdictSagaTimeout]` cap, not by this option.

A consumer opts a single perpetual schedule out of the cap at its call site with `timeout: EdictSchedule.Unbounded`, which always beats this default. See the `edict-authoring` skill for the `Schedule(...)` call site.

## Auditing: enable EDICT023 when you adopt it

Auditing is two calls on a silo: `silo.WithAudit()` arms capture (it writes each decision to the WORM audit store the persistence provider registered), and `silo.Services.AddEdictAudit(resolver)` registers the origin principal resolver. A client that only issues commands registers `AddEdictAudit(resolver)` alone. `edict_describe_silo_wiring` reports `WithAudit` as the silo on-switch, so confirm it is wired (not just the resolver) before you call a capturing silo done. Once wired, the runtime fails closed at the origin: an originating `SendAsync` with no resolved principal throws before anything is persisted. Pair that wiring with the opt-in **`EDICT023`** analyzer so the same gap is caught at compile time instead of at runtime. It is off by default — an analyzer cannot see that `AddEdictAudit` was wired in this (often separate) assembly — so enable it per project in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.EDICT023.severity = error
```

Once enabled it flags every bare `IEdictSender.SendAsync(command)`. Supply a principal explicitly with the `SendAsync(command, principal)` overload for a context-free origin (worker, import, admin script), or silence a resolver-backed site you have confirmed is attributed at runtime with `[SuppressMessage("Edict", "EDICT023")]`. Enable it only in the projects that adopt auditing; leaving it off elsewhere is correct, not an omission.

## Telemetry wiring

Edict exposes one `ActivitySource` and one `Meter`, both named `"Edict"` (`EdictDiagnostics.SourceName`). Register exactly those on your OpenTelemetry builder, on both the silo and any Web front end:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(EdictDiagnostics.SourceName))
    .WithMetrics(metrics => metrics
        .AddMeter(EdictDiagnostics.SourceName)
        .SetExemplarFilter(ExemplarFilterType.TraceBased));
```

Register **only** the Edict source for Edict causality — do not add `AddAspNetCoreInstrumentation()` expecting it to root your command traces. Edict already roots a trace at `edict.command`, and adding the AspNetCore source layers detached `HttpRequestIn` spans over it. The `TraceBasedExemplarFilter` is what lets an operator pivot from a slow metric bucket to a representative trace.

Edict's trace model is **one trace per grain turn, linked across turn boundaries**: each grain turn (command-handle, event-handle, schedule/saga-timeout fire) is its own bounded trace, and an `ActivityLink` connects it to the turn that caused it, rather than nesting everything under the first command. The practical wiring consequence is sampling: each trace makes its own head decision, so head sampling at `edict.command` is your volume lever, but to keep a whole link-group together at the collector you run **tail sampling or a link-aware sampler**. The model and the operator-side detail are in `telemetry.md` and `observability.md` (the latter also maps the substrate meters to wire alongside the Edict meter).

## Supported pairings

Pick exactly one streaming + one persistence:

- Kafka + Postgres
- Kafka + Azure Persistence
- Azure Streaming + Postgres
- Azure Streaming + Azure Persistence

A silo that wires two streaming providers, two persistence providers, or that wires `AddEdictKafkaStreams` without any persistence, is unsupported and outside the conformance-proven matrix.

## Client wiring

The client only needs `AddEdict()` plus the contract-assembly registration on the serializer (`serializer.AddAssembly(typeof(I{Name}CommandHandler).Assembly)` and `serializer.AddEdictContractSerializer()`). Streaming and persistence extensions are silo-only — do not add them on the client.

## See also

- For the contract attributes the silo's generator pipeline reads: see the `edict-contracts` skill.
- For the grain roles wired into the silo: see the `edict-authoring` skill.
- For testing the wired silo: see the `edict-testing` skill.
- For the options each `AddEdict*` call accepts, their defaults, and their validation rules: see the `docs/configuration` folder in the Edict repository.
