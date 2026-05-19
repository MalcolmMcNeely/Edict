using Edict.Contracts.Events;
using Edict.Core.Outbox;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Testing;

/// <summary>
/// Decorates the bare <see cref="OutboxEffectKind.PublishEvent"/> executor: it
/// records the event on the timeline, then delegates to the real executor so
/// the genuine engine still publishes to the memory stream. The single choke
/// point for every domain Event the consumer raises (ADR 0018), mirroring how
/// <see cref="RecordingEdictSender"/> is the choke point for Commands. Chaos
/// (seeded duplicate / out-of-order delivery) layers in here in a later slice.
/// </summary>
sealed class RecordingPublishEventExecutor(
    IOutboxEffectExecutor inner,
    Serializer serializer,
    EdictTimelineRecorder recorder) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider)
    {
        recorder.RecordEvent(serializer.Deserialize<EdictEvent>(entry.Payload));
        return inner.ExecuteAsync(entry, streamProvider);
    }
}
