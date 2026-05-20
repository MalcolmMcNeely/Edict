using Edict.Contracts.Events;

using Orleans.Streams;

namespace Edict.Core.Outbox;

/// <summary>
/// Executes one drained Outbox effect. One implementation per
/// <see cref="OutboxEffectKind"/>; the <see cref="OutboxHost{TPayload}"/>
/// resolves the executor by the head entry's <see cref="OutboxEntry.Kind"/>
/// (ADR 0018). The host passes the two grain-instance-bound primitives an
/// executor might need — the hosting grain's stream provider (for
/// <see cref="OutboxEffectKind.PublishEvent"/>) and the deferred-dispatch
/// callback (for <see cref="OutboxEffectKind.InvokeHandler"/>, ADR 0023) — as
/// explicit parameters; each executor uses only what its effect requires.
/// Bare-named — no consumer types it.
/// </summary>
interface IOutboxEffectExecutor
{
    /// <summary>The effect kind this executor handles.</summary>
    OutboxEffectKind Kind { get; }

    /// <summary>
    /// Performs the side effect. A throw is the drain-failure signal: the
    /// host bumps backoff and stops at the head (causal order preserved).
    /// </summary>
    Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch);
}
