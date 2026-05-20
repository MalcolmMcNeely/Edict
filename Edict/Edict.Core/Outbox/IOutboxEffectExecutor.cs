using Edict.Contracts.Events;

using Orleans.Streams;

namespace Edict.Core.Outbox;

/// <summary>
/// Executes one drained Outbox effect. One implementation per
/// <see cref="OutboxEffectKind"/>; the <see cref="OutboxHost{TPayload}"/>
/// resolves the executor by the entry's <see cref="OutboxEntry.Kind"/>
/// (ADR 0018). The host passes the grain-instance-bound primitives an executor
/// might need — the hosting grain's stream provider (for
/// <see cref="OutboxEffectKind.PublishEvent"/>), the deferred-dispatch callback
/// (for <see cref="OutboxEffectKind.InvokeHandler"/>, ADR 0023), and the
/// consuming grain's CLR <see cref="Type"/> (for the
/// <see cref="OutboxEffectKind.InvokeHandler"/> unwrap predicate after the
/// ADR-0026 fold) — as explicit parameters; each executor uses only what its
/// effect requires. Bare-named — no consumer types it.
/// </summary>
interface IOutboxEffectExecutor
{
    /// <summary>The effect kind this executor handles.</summary>
    OutboxEffectKind Kind { get; }

    /// <summary>
    /// Performs the side effect. A throw is the drain-failure signal: the
    /// host bumps backoff (and continues past this entry — ADR 0026).
    /// </summary>
    Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch,
        Type? consumerType);
}
