# Edict.Core

[Edict](https://github.com/MalcolmMcNeely/Edict) is a CQRS and event-driven framework for .NET on Microsoft Orleans. Edict.Core is its persistence-agnostic engine — the command-handler, saga, event-handler, projection-builder, and idempotency base types, plus the outbox engine.

The Edict source generators and analyzers ride inside this package under `analyzers/dotnet/cs/`, so one reference wires up the full consumer surface.

You also need one streaming package (`Edict.Azure.Streaming` or `Edict.Kafka`) and one persistence package (`Edict.Azure.Persistence` or `Edict.Postgres`).

## Install

```
dotnet add package Edict.Core --prerelease
```

## Learn more

See [docs/usage/getting-started.md](https://github.com/MalcolmMcNeely/Edict/blob/main/docs/usage/getting-started.md) for the smallest valid sample.
