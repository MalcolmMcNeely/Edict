# Edict.Contracts

Consumer-facing contracts for the [Edict](https://github.com/MalcolmMcNeely/Edict) CQRS framework on Microsoft Orleans.

This package carries the command and event base types, the `[EdictRouteKey]` and `[EdictStream]` attributes, and the rejection model. Reference it from any assembly that defines or consumes Edict commands and events — your contracts assembly, your domain assembly, your tests.

## Install

```
dotnet add package Edict.Contracts --prerelease
```

## Learn more

See [docs/usage/getting-started.md](https://github.com/MalcolmMcNeely/Edict/blob/main/docs/usage/getting-started.md) for the smallest valid sample.
