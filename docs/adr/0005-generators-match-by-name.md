# Generators reference nothing; match attributes by fully-qualified name

`Edict.Generators` (source generators + analyzers) targets `netstandard2.0` — a hard Roslyn constraint. It does **not** project-reference `Edict.Abstractions` (net10.0), where `[RouteKey]`, `[Telemeterized]`, `Command`, and `CommandResult` are defined. Instead the generator recognises Edict's annotations by **matching the fully-qualified type name** in the consumer's compilation.

This is deliberate and a future contributor will be tempted to "fix" it by adding the obvious `<ProjectReference>` — which reintroduces the netstandard2.0-vs-net10.0 mismatch and a circular-feeling graph. Do not. The base types must live in a referenced runtime assembly anyway (consumers inherit/return them), so keeping the generator reference-free via FQN matching is the robust Roslyn pattern, not an oversight.

## Considered Options

- **Generator project-references `Edict.Abstractions`** — rejected: TFM mismatch; the analyzer assembly would drag a net10.0 reference it cannot load.
- **Generator emits the attribute definitions via post-initialization output** — rejected: scatters Edict's public surface across two mechanisms and cannot help `Command`/`CommandResult`, which must be referenced regardless. Consistency of the public surface wins.
