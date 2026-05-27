using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Idempotency;

using Orleans;
using Orleans.Streams;

namespace Edict.Kafka.Tests.Resilience;

// Dedicated event type for the Kafka resilience suite. The resilience cluster
// owns its own Kafka container so it can be paused/restarted without affecting
// other collections; the event routes on its own stream so a failure here does
// not contaminate conformance assertions against the assembly-shared Kafka.
[EdictStream("KafkaResilienceEvents")]
public sealed partial record KafkaResilienceTestEvent(Guid AggregateId, int Sequence) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public int Sequence { get; init; } = Sequence;
}

public interface IKafkaResilienceEventPublisher : IGrainWithGuidKey
{
    Task PublishEventAsync(EdictEvent evt);
}

public sealed class KafkaResilienceEventPublisher : Grain, IKafkaResilienceEventPublisher
{
    public Task PublishEventAsync(EdictEvent evt) =>
        this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("KafkaResilienceEvents", this.GetPrimaryKey()))
            .OnNextAsync(evt);
}

public interface IKafkaResilienceTestConsumer : IGrainWithGuidKey
{
    Task<IReadOnlyList<int>> GetHandledSequencesAsync();
}

[ImplicitStreamSubscription("KafkaResilienceEvents")]
public sealed class KafkaResilienceTestConsumer : EdictIdempotencyBase, IKafkaResilienceTestConsumer
{
    readonly List<int> _handledSequences = [];

    protected override int WindowSize => 64;

    protected override Task<bool> DispatchAsync(EdictEvent evt)
    {
        if (evt is not KafkaResilienceTestEvent rEvt)
        {
            return Task.FromResult(false);
        }
        _handledSequences.Add(rEvt.Sequence);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<int>> GetHandledSequencesAsync() =>
        Task.FromResult<IReadOnlyList<int>>(_handledSequences.AsReadOnly());
}

static class KafkaResilienceWaiters
{
    public static async Task<IReadOnlyList<int>> WaitForHandledAsync(
        IKafkaResilienceTestConsumer grain,
        int expectedCount,
        int timeoutSeconds = 120)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var seqs = await grain.GetHandledSequencesAsync();
            if (seqs.Count >= expectedCount)
            {
                return seqs;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        return await grain.GetHandledSequencesAsync();
    }
}
