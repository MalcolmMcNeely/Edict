using Edict.Core.Outbox;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Azure.Tests;

/// <summary>
/// Azure-suite twin of the Core controllable executor: a flippable
/// <see cref="OutboxEffectKind.PublishEvent"/> executor so a conformance test
/// can drive a post-commit publish failure (→ dead-letter) and then a recovery
/// drive against the <b>real Azure Queue stream</b> stack. Delegates to the
/// genuine <see cref="PublishEventExecutor"/> when not failing.
/// </summary>
sealed class AzureControllableOutboxExecutor(Serializer serializer) : IOutboxEffectExecutor
{
    readonly PublishEventExecutor _inner = new(serializer);

    public static volatile bool ShouldFail;

    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("controllable failure (azure conformance test)");
        }

        return _inner.ExecuteAsync(entry, streamProvider);
    }
}
