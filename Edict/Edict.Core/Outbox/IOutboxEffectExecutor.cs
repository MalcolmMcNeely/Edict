using Orleans.Streams;

namespace Edict.Core.Outbox;

/// <summary>
/// Executes one drained Outbox effect. One implementation per
/// <see cref="OutboxEffectKind"/>; the <see cref="OutboxDrainEngine"/> resolves
/// the executor by the head entry's <see cref="OutboxEntry.Kind"/> (ADR 0018).
/// This slice ships only the <see cref="OutboxEffectKind.PublishEvent"/>
/// executor. Bare-named — no consumer types it.
/// </summary>
interface IOutboxEffectExecutor
{
    /// <summary>The effect kind this executor handles.</summary>
    OutboxEffectKind Kind { get; }

    /// <summary>
    /// Performs the side effect. A throw is the drain-failure signal: the
    /// engine bumps backoff and stops at the head (causal order preserved).
    /// </summary>
    Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider);
}
