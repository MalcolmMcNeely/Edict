using Edict.Contracts.Events;
using Edict.Core.Outbox;

using Orleans.Streams;

namespace Edict.Core.Tests.Outbox;

/// <summary>
/// Test seam: records every drained <see cref="OutboxEntry.EntryId"/> against
/// the configured <see cref="OutboxEffectKind"/> so a test can assert that the
/// drain host reached its executor (or did not). The recorded list is
/// process-wide because the cluster builder constructs configurators via
/// reflection and cannot capture closure state.
/// </summary>
sealed class CountingExecutor(OutboxEffectKind kind) : IOutboxEffectExecutor
{
    public static readonly List<Guid> Executed = [];

    public OutboxEffectKind Kind { get; } = kind;

    public Task ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType)
    {
        lock (Executed)
        {
            Executed.Add(entry.EntryId);
        }
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        lock (Executed)
        {
            Executed.Clear();
        }
    }
}
