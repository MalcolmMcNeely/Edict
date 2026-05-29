# Edict.Telemetry

OpenTelemetry types and the `edict.*` tag taxonomy for the [Edict](https://github.com/MalcolmMcNeely/Edict) CQRS framework.

This package carries the `[Telemeterized]` attribute, the canonical tag-key constants, and the metric instruments emitted by the framework. It is a transitive dependency of `Edict.Core` — most consumers never reference it directly.

## Install

```
dotnet add package Edict.Telemetry --prerelease
```

## Learn more

See [docs/usage/getting-started.md](https://github.com/MalcolmMcNeely/Edict/blob/main/docs/usage/getting-started.md) for the smallest valid sample.
