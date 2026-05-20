using Edict.Contracts.Events;
using Edict.Core.Idempotency;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Grains;

public interface IDedupTestConsumer : IGrainWithGuidKey
{
    Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync();
    Task ArmThrowOnNextAsync();
    Task DeactivateSelfAsync();
    Task DeliverAsync(EdictEvent evt);
    Task ForceDrainViaReminderAsync();
    Task<int> GetPendingOutboxCountAsync();
}

public interface IDedupPublisherGrain : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent evt);
}

/// <summary>
/// Test-only grain: publishes any event to the "DedupTest" domain stream.
/// Allows tests to inject events with pre-set EventIds (bypassing the
/// EdictCommandHandler stamp) to exercise dedup mechanics directly.
/// </summary>
public sealed class DedupPublisherGrain : Grain, IDedupPublisherGrain
{
    public Task PublishAsync(EdictEvent evt)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("DedupTest", this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }
}

/// <summary>
/// Minimal hand-written test subclass of <see cref="EdictIdempotencyBase"/>.
/// Ring size is 3 to make eviction behaviour easy to exercise in tests.
/// </summary>
[ImplicitStreamSubscription("DedupTest")]
public sealed class DedupTestConsumer : EdictIdempotencyBase, IDedupTestConsumer
{
    private readonly List<Guid> _handledEventIds = [];
    private bool _throwOnNext;

    protected override int RingSize => 3;

    protected override Task<bool> DispatchAsync(EdictEvent evt)
    {
        if (evt is not DedupTestEvent dedupEvt)
            return Task.FromResult(false);

        if (_throwOnNext)
        {
            _throwOnNext = false;
            throw new InvalidOperationException("simulated dispatch failure");
        }

        _handledEventIds.Add(dedupEvt.EventId);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync() =>
        Task.FromResult<IReadOnlyList<Guid>>(_handledEventIds.AsReadOnly());

    public Task ArmThrowOnNextAsync()
    {
        _throwOnNext = true;
        return Task.CompletedTask;
    }

    public Task DeactivateSelfAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test-only direct-delivery seam: routes the event through the same
    /// <see cref="EdictIdempotencyBase{TPayload}.OnEdictEventAsync"/> path
    /// Orleans's stream-callback uses, so a test can drive consecutive
    /// deliveries deterministically (memory streams suppress repeated
    /// observer-side throws on the same subscription).
    /// </summary>
    public Task DeliverAsync(EdictEvent evt) => OnEdictEventAsync(evt);

    /// <summary>
    /// Drives the engine's reminder-tick drain path deterministically — used
    /// by the missing-blob receiver-side dead-letter test to exhaust
    /// <c>MaxAttempts</c> retries without waiting on Orleans's one-minute
    /// reminder floor (ADR 0026 retry loop).
    /// </summary>
    public Task ForceDrainViaReminderAsync() =>
        ReceiveReminder("edict-outbox-drain", new TickStatus());

    public Task<int> GetPendingOutboxCountAsync() =>
        Task.FromResult(OutboxStateForProbe.Pending.Count);
}
