using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Idempotency;
using Edict.Tests.Conformance.Reactivation;

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

public interface IDedupTestConsumer : IConfirmsDeactivation
{
    Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync();
    Task ArmThrowOnNextAsync();
}

public interface IDedupPublisherGrain : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent edictEvent);
}

public sealed class DedupPublisherGrain : Grain, IDedupPublisherGrain
{
    public Task PublishAsync(EdictEvent edictEvent)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ConformanceDedupTest", this.GetPrimaryKey()));
        return stream.OnNextAsync(edictEvent);
    }
}

[ImplicitStreamSubscription("ConformanceDedupTest")]
public sealed class DedupTestConsumer : EdictIdempotencyBase, IDedupTestConsumer
{
    readonly List<Guid> _handledEventIds = [];
    bool _throwOnNext;
    Guid _activationId;

    protected override int WindowSize => 3;

    // Fresh per activation so the deactivate-and-confirm seam can poll until it
    // changes to confirm the ring genuinely reloaded from durable state.
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationId = Guid.NewGuid();
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<Guid> GetActivationIdAsync() => Task.FromResult(_activationId);

    protected override Task<EdictDispatchOutcome> DispatchAsync(EdictEvent edictEvent)
    {
        if (edictEvent is not DedupTestEvent dedupEvt)
        {
            return Task.FromResult(EdictDispatchOutcome.NotHandled);
        }

        if (_throwOnNext)
        {
            _throwOnNext = false;
            throw new InvalidOperationException("simulated dispatch failure");
        }

        _handledEventIds.Add(dedupEvt.EventId);
        return Task.FromResult(EdictDispatchOutcome.HandledWithNoEffect);
    }

    public Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync() =>
        Task.FromResult<IReadOnlyList<Guid>>(_handledEventIds.AsReadOnly());

    public Task ArmThrowOnNextAsync()
    {
        _throwOnNext = true;
        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
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

    // Gates on the sentinel id itself rather than a handled count: a count a leaked
    // redelivery could also satisfy would let the sentinel-after assertion pass while
    // masking the very leak it guards against.
    public static async Task<IReadOnlyList<Guid>> WaitForHandledIdAsync(
        IDedupTestConsumer consumer,
        Guid expectedId,
        int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await consumer.GetHandledEventIdsAsync();
            if (ids.Contains(expectedId))
            {
                return ids;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await consumer.GetHandledEventIdsAsync();
    }
}
