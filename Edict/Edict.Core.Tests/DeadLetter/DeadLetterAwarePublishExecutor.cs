using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Outbox;

using Orleans.Serialization;

namespace Edict.Core.Tests.DeadLetter;

// Test executor that fails every PublishEvent EXCEPT EdictDeadLetterRaised
// (ADR 0022). The point of the end-to-end test is to drive the engine's
// promotion path on a real event: the original publish fails permanently, the
// engine promotes at MaxAttempts to an EdictDeadLetterRaised entry, and that
// notification must itself publish cleanly so the projection grain can write
// the row. Delegates to the real PublishEventExecutor for the dead-letter
// notification.
sealed class DeadLetterAwarePublishExecutor(Serializer serializer) : IOutboxEffectExecutor
{
    readonly PublishEventExecutor _inner = new(serializer);

    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task ExecuteAsync(OutboxEntry entry, IOutboxHost host)
    {
        var evt = serializer.Deserialize<EdictEvent>(entry.Payload);
        if (evt is EdictDeadLetterRaised)
        {
            return _inner.ExecuteAsync(entry, host);
        }
        throw new InvalidOperationException("simulated permanent publish failure");
    }
}
