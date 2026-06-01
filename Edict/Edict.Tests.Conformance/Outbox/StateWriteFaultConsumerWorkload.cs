using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Idempotency;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Tests.Conformance.Outbox;

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Outbox.WriteFaultConsumerState")]
public sealed class WriteFaultConsumerState : IEdictPersistedState
{
    [Id(0)]
    public int AppliedCount { get; set; }
}

[EdictStream("ConformanceStateWriteFault")]
public sealed partial record StateWriteFaultEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

public interface IStateWriteFaultConsumer : IGrainWithGuidKey
{
    Task<Guid> GetActivationIdAsync();
    Task<int> GetAppliedCountAsync();
}

public interface IStateWriteFaultPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent edictEvent);
}

public sealed class StateWriteFaultPublisher : Grain, IStateWriteFaultPublisher
{
    public Task PublishAsync(EdictEvent edictEvent)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ConformanceStateWriteFault", this.GetPrimaryKey()));
        return stream.OnNextAsync(edictEvent);
    }
}

// Consumer that mutates its durable payload inside the handling turn, before the
// commit write — the saga-Progress / projection-row double-apply shape. It
// stages no effect, so the dedup-ring slot and the payload ride one inline
// write. A write fault leaves the payload mutation dirty in memory; only
// dropping the activation can discard it before a redelivery re-applies it.
[ImplicitStreamSubscription("ConformanceStateWriteFault")]
public sealed class StateWriteFaultConsumer : EdictIdempotencyBase<WriteFaultConsumerState>, IStateWriteFaultConsumer
{
    Guid _activationId;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationId = Guid.NewGuid();
        await base.OnActivateAsync(cancellationToken);
    }

    protected override Task<EdictDispatchOutcome> DispatchAsync(EdictEvent edictEvent)
    {
        if (edictEvent is not StateWriteFaultEvent)
        {
            return Task.FromResult(EdictDispatchOutcome.NotHandled);
        }

        State.Payload.AppliedCount++;
        return Task.FromResult(EdictDispatchOutcome.HandledWithNoEffect);
    }

    public Task<Guid> GetActivationIdAsync() => Task.FromResult(_activationId);

    public Task<int> GetAppliedCountAsync() => Task.FromResult(State.Payload.AppliedCount);
}
