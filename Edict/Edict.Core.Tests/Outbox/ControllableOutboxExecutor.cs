using Edict.Core.Outbox;

using Orleans.Serialization;

namespace Edict.Core.Tests.Outbox;

/// <summary>
/// Test seam: a <see cref="OutboxEffectKind.PublishEvent"/> executor whose
/// failure is flippable, so a test can drive a post-commit publish failure and
/// then a recovery drain. Delegates to the real <see cref="PublishEventExecutor"/>
/// when not failing, so a successful drain is genuinely published.
/// </summary>
sealed class ControllableOutboxExecutor(Serializer serializer) : IOutboxEffectExecutor
{
    readonly PublishEventExecutor _inner = new(serializer);

    public static volatile bool ShouldFail;
    public static int FailedAttempts;

    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task ExecuteAsync(OutboxEntry entry, IOutboxHost host)
    {
        if (ShouldFail)
        {
            Interlocked.Increment(ref FailedAttempts);
            throw new InvalidOperationException("controllable failure (test)");
        }

        return _inner.ExecuteAsync(entry, host);
    }
}
