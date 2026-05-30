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
