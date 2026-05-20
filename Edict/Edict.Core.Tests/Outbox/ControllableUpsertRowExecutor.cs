using Edict.Contracts.Events;
using Edict.Core.Outbox;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Tests.Outbox;

/// <summary>
/// Test seam: a <see cref="OutboxEffectKind.UpsertRow"/> executor whose failure
/// is flippable, so a test can drive a crash between the ring/outbox commit and
/// the row write, then a recovery drain (ADR 0018, the ADR-0012 gap closure).
/// Counts attempts so a test can prove the same effect being executed more than
/// once still applies the row exactly once (idempotent by pk/rk). Delegates to
/// the real <see cref="UpsertRowExecutor"/> when not failing, so a successful
/// drain genuinely writes the row.
/// </summary>
sealed class ControllableUpsertRowExecutor(Serializer serializer, IServiceProvider services) : IOutboxEffectExecutor
{
    readonly UpsertRowExecutor _inner = new(serializer, services);

    public static volatile bool ShouldFail;

    /// <summary>Applies the effect twice on a successful drain, simulating
    /// at-least-once duplicate delivery so a test can prove the pk/rk upsert
    /// is idempotent (the row is not double-applied).</summary>
    public static volatile bool DuplicateOnSuccess;

    public static int Attempts;

    public OutboxEffectKind Kind => OutboxEffectKind.UpsertRow;

    public async Task ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch)
    {
        Interlocked.Increment(ref Attempts);

        if (ShouldFail)
        {
            throw new InvalidOperationException("controllable upsert failure (test)");
        }

        await _inner.ExecuteAsync(entry, streamProvider, deferredDispatch);

        if (DuplicateOnSuccess)
        {
            await _inner.ExecuteAsync(entry, streamProvider, deferredDispatch);
        }
    }
}
