namespace Edict.Contracts.Persistence;

/// <summary>
/// Marker for any consumer-authored type Edict persists durably: aggregate state
/// behind <c>EdictCommandHandler&lt;TState&gt;</c>, saga progress behind
/// <c>EdictSaga&lt;TProgress&gt;</c>, and the row POCO behind
/// <c>EdictTableProjectionBuilder&lt;T&gt;</c>. The marker anchors
/// the attribute-placement policy: the framework generator only emits attribute
/// values that are safe to recompute from current syntax (<c>[Alias]</c> and
/// <c>[MessagePackObject(true)]</c> on commands and events); anything that must
/// stay stable across a class rename — the frozen-literal <c>[Alias]</c>, the
/// per-property <c>[Id(n)]</c>, the <c>[GenerateSerializer]</c> opt-in — is the
/// consumer's responsibility on every <see cref="IEdictPersistedState"/>
/// implementer. EDICT011 enforces the consumer half at compile time.
/// </summary>
public interface IEdictPersistedState;
