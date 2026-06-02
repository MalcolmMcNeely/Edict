# Azure streaming wiring

The Azure streaming side ships in `Edict.Azure.Streaming` and is wired through two `ISiloBuilder` extensions: `AddEdictAzureStreams` for the Orleans Azure Queue Storage stream provider plus the claim-check threshold, and `AddEdictAzureBlobClaimCheck` for the Azure-blob-backed claim-check store. The store is split off so a silo running Azure streams with Postgres persistence can skip it and let `AddEdictPostgresPersistence` register a Postgres-backed store instead.

## Silo setup

```csharp
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.Streaming;
using Edict.Azure.Streaming.ClaimCheck;
using Edict.Core;
using Edict.Core.Serialization;

using Orleans.Serialization;

Host.CreateDefaultBuilder(args)
    .UseOrleans((context, silo) =>
    {
        silo.UseLocalhostClustering();
        silo.Services.AddSerializer(ser =>
        {
            ser.AddAssembly(typeof(OrderCommandHandler).Assembly);
            ser.AddEdictContractSerializer();
        });

        silo.AddEdict();

        silo.AddEdictAzureStreams(o =>
        {
            o.QueueServiceClient = new QueueServiceClient(context.Configuration.GetConnectionString("queues"));
        });

        silo.AddEdictAzureBlobClaimCheck(o =>
        {
            o.BlobServiceClient = new BlobServiceClient(context.Configuration.GetConnectionString("blobs"));
        });

        // Pair with one of: AddEdictAzurePersistence | AddEdictPostgresPersistence.
    });
```

## Client setup

The client process does not call `AddEdictAzureStreams` or `AddEdictAzureBlobClaimCheck` — neither extension is on the consumer hot path. The client only registers the Edict serializer and the consumer's command-handler interface assembly so the Orleans grain-call codec resolves consumer types.

```csharp
using Edict.Core;
using Edict.Core.Serialization;

using Orleans.Serialization;

builder.UseOrleansClient(client =>
{
    client.UseLocalhostClustering();
    client.Services.AddSerializer(ser =>
    {
        ser.AddAssembly(typeof(IOrderCommandHandler).Assembly);
        ser.AddEdictContractSerializer();
    });
});

builder.Services.AddEdict();
```

## Configuration

`EdictAzureStreamsOptions` (the claim-check threshold and the queue fan-out / poll knobs), `EdictAzureBlobClaimCheckOptions` (the claim-check container), the connection-string precedence rules, and the cost-vs-latency tuning guidance are documented in [configuration/azure-streaming.md](../../configuration/azure-streaming.md).

## Gotchas

### Claim-check store builds eagerly on the host thread

`AddEdictAzureBlobClaimCheck` calls `AzureBlobClaimCheckStore.CreateAsync(...).GetAwaiter().GetResult()` at silo-wiring time, on the host thread, and registers the resulting store as a singleton instance. A factory-lambda registration that called the same `.GetAwaiter().GetResult()` lazily at first activation would deadlock — the Orleans grain task scheduler cannot resume the `await` continuation while the activation thread is blocked. The eager-build pattern is the only safe form; do not replace it with a factory lambda when forking this extension.

### AQS streams do not propagate `Activity.Current` across the hop

The Azure Queue Storage stream substrate carries no W3C trace-context headers. Edict reconstitutes trace continuity by reading `evt.TraceId` and `evt.SpanId` off the event itself (stamped at `Raise` time inside the publisher's `edict.command` span) and restoring the parent context on the receiver. Any deferred-dispatch path that builds outbox entries from `Activity.Current` instead of the event fields will record `00000000…` traceparents — the receiver then opens a root span and the publisher → handle link is lost. This affects framework authors forking the deferred-dispatch executors, not consumers writing `HandleAsync` methods.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Event`, `Event Envelope`, `Claim Check`, `Outbox`.
- Concepts — [events.md](../concepts/events.md), [claim-check.md](../concepts/claim-check.md), [telemetry.md](../concepts/telemetry.md).
- Configuration — [azure-streaming.md](../../configuration/azure-streaming.md) — the options tables, tuning guidance, and connection-string rules.
- Wiring — [azure-persistence.md](azure-persistence.md), [postgres.md](postgres.md).
- ADRs — [0019 — Deferred dispatch](../../adr/0019-deferred-dispatch.md), [0020 — Claim check for oversized events](../../adr/0020-claim-check.md), [0023 — Config surface and installation](../../adr/0023-config-surface-and-installation.md), [0042 — Azure package split](../../adr/0042-azure-package-split.md).
