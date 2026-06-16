# Edict.Postgres

[Edict](https://github.com/MalcolmMcNeely/Edict) is a CQRS and event-driven framework for .NET on Microsoft Orleans. Edict.Postgres is its PostgreSQL projection store, Orleans grain persistence, reminders, and tamper-evident audit store.

Pair this with any Edict streaming package — `Edict.Kafka` for a Kafka + Postgres deployment, or `Edict.Azure.Streaming` for AQS streaming with Postgres state.

## Install

```
dotnet add package Edict.Postgres --prerelease
```

## Learn more

See [docs/usage/getting-started.md](https://github.com/MalcolmMcNeely/Edict/blob/main/docs/usage/getting-started.md) for the smallest valid sample.
