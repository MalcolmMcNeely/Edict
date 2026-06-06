# Configuration

Every Edict options class is documented here, organised per provider. A consumer reads only the pages matching their chosen streaming + persistence pairing — pick the streaming page and the persistence page for your pair, plus `core.md` for the provider-agnostic knobs.

## The installation surface

A silo wires Edict through a small, fixed set of `ISiloBuilder` extensions:

| Call | What it registers | Options |
| --- | --- | --- |
| `AddEdict()` | The provider-agnostic grain runtime — command/event handling, the outbox, idempotency dedup, and saga timeouts. | [`core.md`](core.md) — `EdictOptions`, `EdictSagaOptions` |
| `AddEdict{…}Streams(...)` | The wire substrate. One of `AddEdictAzureStreams` or `AddEdictKafkaStreams`. | [`azure-streaming.md`](azure-streaming.md) or [`kafka.md`](kafka.md) |
| `AddEdict{…}Persistence(...)` | Grain-state storage, the dead-letter repository, the table write-store, and reminders. One of `AddEdictAzurePersistence` or `AddEdictPostgresPersistence`. | [`azure-persistence.md`](azure-persistence.md) or [`postgres.md`](postgres.md) |

An Azure-streaming silo that persists on Azure adds one more call, `AddEdictAzureBlobClaimCheck(...)`, for the blob-backed claim-check store; on the Postgres pairing the store comes from `AddEdictPostgresPersistence` instead. Its knobs are documented alongside the streams options in [`azure-streaming.md`](azure-streaming.md).

Pick one streaming package and one persistence package — the four supported pairings and their `dotnet add package` lines are in [getting-started.md](../usage/getting-started.md). Each page here links back to its [wiring](../usage/wiring/azure-streaming.md) counterpart for the `Add*` call shape and the framework-author gotchas.

## How configuration is applied

Each extension takes an `Action<T>` over its options class:

```csharp
silo.AddEdict(options =>
{
    options.IdempotencyWindowSize = 256;
});
```

The `Action<T>` composes with the standard options pipeline, so a consumer who prefers binding from configuration can call `services.Configure<EdictOptions>(configuration.GetSection("Edict"))` and the `Action<T>` overlays on top of the bound values.

## Fail-fast validation

Edict validates its configuration once, at host start, through a hosted service (`EdictWiringValidator`). It accumulates every problem across every options bag and every missing-provider check into one aggregated `EdictWiringException` — a silo with two misconfigurations fails once on startup with both listed, not once per `HandleAsync` call.

Validation never silently clamps an out-of-range value. An `OutboxJitterFraction` outside `[0, 1]`, an `OutboxBaseDelay` above `OutboxMaxDelay`, an `OutboxDrainReminderPeriod` below Orleans' one-minute reminder floor, a non-positive `IdempotencyWindowSize` or `OutboxMaxAttempts` — each is a startup failure with a named message, never a quietly corrected value. A misconfigured silo does not boot.

## Check it before you boot

`EdictWiringValidator` is the ground-truth verdict, but it only fires once the host can start. To catch a missing required knob or a known footgun *before* that, your agent can run the `edict_check_configuration` MCP tool: it reads `Program.cs`, works out which of the very knobs documented on these pages you have actually set inside each `AddEdict*` call, and returns a best-effort verdict of required-but-unset knobs, footgun assignments, and incomplete extension combinations. It resolves only set-versus-not-set and defers to `EdictWiringValidator` as ground truth. See [MCP tools](../usage/agentic/mcp-tools.md#edict_check_configuration).

## See also

- [getting-started.md](../usage/getting-started.md) — the supported pairing matrix and `dotnet add package` lines.
- ADR — [0023 — Config surface and installation](../adr/0023-config-surface-and-installation.md).
