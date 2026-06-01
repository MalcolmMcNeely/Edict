# Edict.Contracts

[Edict](https://github.com/MalcolmMcNeely/Edict) is a CQRS and event-driven framework for .NET on Microsoft Orleans. Edict.Contracts is its consumer-typed wire surface — command and event base types, the `[EdictRouteKey]` and `[EdictStream]` attributes, and the rejection model.

Reference this package from any assembly that defines or consumes Edict commands and events — your contracts assembly, your domain assembly, your tests.

## Install

```
dotnet add package Edict.Contracts --prerelease
```

## Learn more

See [docs/usage/getting-started.md](https://github.com/MalcolmMcNeely/Edict/blob/main/docs/usage/getting-started.md) for the smallest valid sample.
