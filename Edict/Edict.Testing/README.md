# Edict.Testing

[Edict](https://github.com/MalcolmMcNeely/Edict) is a CQRS and event-driven framework for .NET on Microsoft Orleans. Edict.Testing is its in-memory test framework — TestCluster fixtures, projection and saga probes, a deterministic-timeline alternative to `Task.Delay`, and the chaos controls (bounded reorder, duplicate delivery) that model at-least-once delivery.

Install it in your test project alongside one Edict streaming and one Edict persistence package.

## Install

```
dotnet add package Edict.Testing --prerelease
```

## Learn more

See [docs/usage/getting-started.md](https://github.com/MalcolmMcNeely/Edict/blob/main/docs/usage/getting-started.md) for the smallest valid sample.
