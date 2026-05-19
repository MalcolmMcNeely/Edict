# Edict.Generators

Roslyn incremental source generators for the Edict framework. All generators match Edict types by fully-qualified name; they carry no runtime reference to any Edict assembly (ADR 0005).

---

## Generators

### `EdictCommandGenerator`

**ADR:** ADR 0005, ADR 0004

**Trigger predicate:** `partial class` whose direct base type is `Edict.Core.Grains.EdictCommandHandler` and that has at least one `Handle(TCommand)` method returning `Task<EdictCommandResult>`.

**Emitted shape (per grain):**

```csharp
// {Namespace}.{TypeName}.g.cs
public partial interface I{TypeName} : global::Edict.Core.Grains.IEdictCommandHandler { }

public partial class {TypeName} : I{TypeName}
{
    public override async Task<EdictCommandResult> DispatchAsync(EdictCommand command) { … }
}

// {Namespace}.{CommandName}.Alias.g.cs  (one per handled command)
[global::Orleans.AliasAttribute("{CommandName}")]
public partial record {CommandName};

// Edict.Generated.AddEdict.g.cs  (one per compilation, aggregates all grains)
public static class EdictServiceCollectionExtensions
{
    public static IServiceCollection AddEdict(this IServiceCollection services) { … }
}
```

---

### `EdictEventGenerator`

**ADR:** ADR 0011, ADR 0010, ADR 0005

**Trigger predicate:** `partial record` (non-`abstract`) whose base type chain includes `Edict.Contracts.Events.EdictEvent`.

**Emitted shape (per event):**

```csharp
// {Namespace}.{EventName}.Alias.g.cs
[global::Orleans.AliasAttribute("{EventName}")]
public partial record {EventName};
```

Discovery is semantic and assembly-wide: the generator emits an alias even when nothing in the same assembly handles the event, so the publisher always has a stable wire identity (ADR 0010).

---

### `EdictProjectionGenerator`

**ADR:** ADR 0011, ADR 0005

**Trigger predicate:** `partial class` whose base type chain includes `Edict.Core.Grains.EdictProjectionBuilder` and that has at least one `Handle(TEvent)` method returning `Task`.

**Emitted shape (per projection grain):**

```csharp
// {Namespace}.{TypeName}.g.cs
public partial interface I{TypeName} : global::Edict.Core.Grains.IEdictProjectionBuilder { }

[global::Orleans.ImplicitStreamSubscriptionAttribute("{StreamName}")]
public partial class {TypeName} : I{TypeName}
{
    protected override async Task SubscribeToStreamAsync(CancellationToken ct) { … }
    protected override async Task<bool> DispatchAsync(EdictEvent evt) { … }
}
```

Multiple `Handle` overloads for events on the same stream share a single `[ImplicitStreamSubscription]` — the generator deduplicates by stream name.
