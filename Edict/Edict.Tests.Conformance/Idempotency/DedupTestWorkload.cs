using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Idempotency;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Tests.Conformance.Idempotency;

[EdictStream("ConformanceDedupTest")]
public sealed partial record DedupTestEvent(Guid AggregateId, int Sequence) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public int Sequence { get; init; } = Sequence;
}

// Event type the dedup consumer does not implement Handle for. Used to prove
// the ring is not consumed by events the consumer doesn't actually dispatch.
[EdictStream("ConformanceDedupTest")]
public sealed partial record UnhandledDedupTestEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

public interface IDedupTestConsumer : IGrainWithGuidKey
{
    Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync();
    Task ArmThrowOnNextAsync();
    Task DeactivateSelfAsync();
}

public interface IDedupPublisherGrain : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent evt);
}

public sealed class DedupPublisherGrain : Grain, IDedupPublisherGrain
{
    public Task PublishAsync(EdictEvent evt)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ConformanceDedupTest", this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }
}

[ImplicitStreamSubscription("ConformanceDedupTest")]
public sealed class DedupTestConsumer : EdictIdempotencyBase, IDedupTestConsumer
{
    readonly List<Guid> _handledEventIds = [];
    bool _throwOnNext;

    protected override int WindowSize => 3;

    protected override Task<bool> DispatchAsync(EdictEvent evt)
    {
        if (evt is not DedupTestEvent dedupEvt)
        {
            return Task.FromResult(false);
        }

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
}

static class DedupTestWaiters
{
    public static async Task<IReadOnlyList<Guid>> WaitForHandledCountAsync(
        IDedupTestConsumer consumer,
        int expectedCount = 1,
        int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await consumer.GetHandledEventIdsAsync();
            if (ids.Count >= expectedCount)
            {
                return ids;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await consumer.GetHandledEventIdsAsync();
    }
}
