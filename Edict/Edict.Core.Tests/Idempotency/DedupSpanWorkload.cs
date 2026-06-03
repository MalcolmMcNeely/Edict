using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Idempotency;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Idempotency;

[EdictStream("DedupSpanTest")]
public sealed partial record DedupSpanTestEvent(Guid AggregateId, int Sequence) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public int Sequence { get; init; } = Sequence;
}

public interface IDedupSpanConsumer : IGrainWithGuidKey
{
    Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync();
}

public interface IDedupSpanPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent edictEvent);
}

public sealed class DedupSpanPublisher : Grain, IDedupSpanPublisher
{
    public Task PublishAsync(EdictEvent edictEvent)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("DedupSpanTest", this.GetPrimaryKey()));
        return stream.OnNextAsync(edictEvent);
    }
}

[ImplicitStreamSubscription("DedupSpanTest")]
public sealed class DedupSpanConsumer : EdictIdempotencyBase, IDedupSpanConsumer
{
    readonly List<Guid> _handledEventIds = [];

    protected override int WindowSize => 3;

    protected override Task<EdictDispatchOutcome> DispatchAsync(EdictEvent edictEvent)
    {
        if (edictEvent is not DedupSpanTestEvent dedupEvent)
        {
            return Task.FromResult(EdictDispatchOutcome.NotHandled);
        }

        _handledEventIds.Add(dedupEvent.EventId);
        return Task.FromResult(EdictDispatchOutcome.HandledWithNoEffect);
    }

    public Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync() =>
        Task.FromResult<IReadOnlyList<Guid>>(_handledEventIds.AsReadOnly());
}

static class DedupSpanWaiters
{
    public static async Task WaitForHandledCountAsync(
        IDedupSpanConsumer consumer, int expectedCount = 1, int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await consumer.GetHandledEventIdsAsync();
            if (ids.Count >= expectedCount)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
