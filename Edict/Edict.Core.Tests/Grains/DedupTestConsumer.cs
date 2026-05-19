using Edict.Contracts.Events;
using Edict.Core.Idempotency;
using Orleans;
using Orleans.Streams;

namespace Edict.Core.Tests.Grains;

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

    protected override async Task SubscribeToStreamAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("DedupTest", this.GetPrimaryKey()));
        await stream.SubscribeAsync(OnStreamEventAsync, static _ => Task.CompletedTask);
    }

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
}
