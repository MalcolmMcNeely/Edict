using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Idempotency;
using Edict.Core.Outbox;

using MessagePack;

using Orleans;
using Orleans.Streams;

namespace Edict.Core.Tests.Grains;

// Exercises the event arm of block-intake (ADR 0019): an EdictIdempotencyBase
// consumer whose DeadLetter slice can be seeded to the cap so a redelivered
// event is NOT acked (ring slot never committed) — nothing silently dropped.

[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("BlockIntake")]
public sealed partial record BlockIntakeEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

public interface IBlockIntakePublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent evt);
}

public interface IBlockableConsumer : IGrainWithGuidKey
{
    Task SeedDeadLetterToCapAsync();
    Task<int> GetHandledCountAsync();
    Task<bool> RingContainsAsync(Guid eventId);
}

public sealed class BlockIntakePublisher : Grain, IBlockIntakePublisher
{
    public Task PublishAsync(EdictEvent evt)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("BlockIntake", this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }
}

[ImplicitStreamSubscription("BlockIntake")]
public sealed class BlockableConsumer : EdictIdempotencyBase, IBlockableConsumer
{
    int _handledCount;

    protected override Task SubscribeToStreamAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("BlockIntake", this.GetPrimaryKey()));
        return stream.SubscribeAsync(OnStreamEventAsync, static _ => Task.CompletedTask);
    }

    protected override Task<bool> DispatchAsync(EdictEvent evt)
    {
        if (evt is not BlockIntakeEvent)
        {
            return Task.FromResult(false);
        }

        _handledCount++;
        return Task.FromResult(true);
    }

    // Probe: fills the DeadLetter slice to the cap-of-1 fixture's limit so the
    // next stream delivery hits the block-intake gate.
    public async Task SeedDeadLetterToCapAsync()
    {
        State.Outbox = State.Outbox with
        {
            DeadLetter = [new DeadLetterEntry
            {
                Entry = new OutboxEntry { EntryId = Guid.NewGuid(), Kind = OutboxEffectKind.PublishEvent },
                DeadLetteredAt = DateTimeOffset.UtcNow,
                Reason = "seeded (test)",
            }],
        };
        await WriteStateAsync();
    }

    public Task<int> GetHandledCountAsync() => Task.FromResult(_handledCount);

    public Task<bool> RingContainsAsync(Guid eventId) =>
        Task.FromResult(State.Payload.Ring.Ring.Contains(eventId));
}
