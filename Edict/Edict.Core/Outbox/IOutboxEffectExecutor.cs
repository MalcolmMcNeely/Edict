namespace Edict.Core.Outbox;

/// <summary>
/// Executes one drained Outbox effect. One implementation per
/// <see cref="OutboxEffectKind"/>; the <see cref="OutboxDrainEngine"/> resolves
/// the executor by the head entry's <see cref="OutboxEntry.Kind"/> (ADR 0018).
/// The host is passed in so an effect that has to call back into the grain
/// (e.g. <see cref="OutboxEffectKind.InvokeHandler"/>'s deferred dispatch into
/// the host's idempotent-consumer surface, ADR 0023) has a single seam to
/// reach the host through; the stream provider is read off the host. Bare-named —
/// no consumer types it.
/// </summary>
interface IOutboxEffectExecutor
{
    /// <summary>The effect kind this executor handles.</summary>
    OutboxEffectKind Kind { get; }

    /// <summary>
    /// Performs the side effect. A throw is the drain-failure signal: the
    /// engine bumps backoff and stops at the head (causal order preserved).
    /// </summary>
    Task ExecuteAsync(OutboxEntry entry, IOutboxHost host);
}
