using Edict.Contracts.Events;
using Edict.Core.Outbox;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Azure.Tests;

/// <summary>
/// Azure-suite twin of the Core controllable UpsertRow executor: a flippable
/// <see cref="OutboxEffectKind.UpsertRow"/> executor so the conformance test can
/// simulate a crash between the ring/outbox commit and the row write, then a
/// recovery drain against the <b>real Azure Queue + Azure Table</b> stack
///. Delegates to the genuine <see cref="UpsertRowExecutor"/>
/// when not failing.
/// </summary>
sealed class AzureControllableUpsertRowExecutor(
    Serializer serializer,
    IServiceProvider services) : IOutboxEffectExecutor
{
    readonly UpsertRowExecutor _inner = new(serializer, services);

    public static volatile bool ShouldFail;

    public OutboxEffectKind Kind => OutboxEffectKind.UpsertRow;

    public Task ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("controllable upsert failure (azure conformance test)");
        }

        return _inner.ExecuteAsync(entry, streamProvider, deferredDispatch, consumerType, liveWireEvent);
    }
}
