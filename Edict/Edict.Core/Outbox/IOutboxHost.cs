using Orleans.Streams;

namespace Edict.Core.Outbox;

/// <summary>
/// The grain-coupled seam the <see cref="OutboxDrainEngine"/> drives. The
/// command-handler / idempotency base implements it explicitly so the engine
/// stays a plain, unit-testable class while the grain owns the single
/// <c>WriteStateAsync</c>, the stream provider and the lazy drain Reminder
/// (ADR 0018). Bare-named — no consumer types it.
/// </summary>
interface IOutboxHost
{
    /// <summary>The Outbox slice of the grain envelope; set is staged, not yet committed.</summary>
    OutboxSlice Outbox { get; set; }

    /// <summary>The grain's "edict" stream provider for <see cref="OutboxEffectKind.PublishEvent"/>.</summary>
    IStreamProvider StreamProvider { get; }

    /// <summary>
    /// The hosting grain's Guid key serialised as a string — carried on every
    /// dead-letter notification so an operator can identify the source aggregate
    /// from a fleet-wide listing (ADR 0022).
    /// </summary>
    string GrainKey { get; }

    /// <summary>
    /// The hosting grain class's full type name — carried on every dead-letter
    /// notification alongside <see cref="GrainKey"/> for source-aggregate
    /// identification (ADR 0022).
    /// </summary>
    string GrainTypeName { get; }

    /// <summary>Persists the whole envelope <c>{ State, Outbox }</c> in one atomic write.</summary>
    Task CommitAsync();

    /// <summary>Registers the lazy crash-recovery drain Reminder (idempotent).</summary>
    Task RegisterDrainReminderAsync();

    /// <summary>Unregisters the drain Reminder; a cheap no-op when none is registered.</summary>
    Task UnregisterDrainReminderAsync();
}
