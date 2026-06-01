# Edict.Azure.Streaming

[Edict](https://github.com/MalcolmMcNeely/Edict) is a CQRS and event-driven framework for .NET on Microsoft Orleans. Edict.Azure.Streaming is its Azure Queue Storage stream provider and blob-based claim-check store.

Pair this with any Edict persistence package — `Edict.Azure.Persistence` for an all-Azure deployment, or `Edict.Postgres` for AQS streaming with Postgres state.

## Install

```
dotnet add package Edict.Azure.Streaming --prerelease
```

## Learn more

See [docs/usage/getting-started.md](https://github.com/MalcolmMcNeely/Edict/blob/main/docs/usage/getting-started.md) for the smallest valid sample.
