using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Pairing.Tests;

/// <summary>
/// Bucket-4 irreducible interaction: a consumer-side grain-state write fault on a
/// <strong>real</strong> persistence provider conjoined with a
/// <strong>real</strong> stream redelivery. The handler mutates its durable
/// payload, then the commit write faults out to the stream layer (it is an
/// infrastructure fault, not dead-lettered, so the framework drops the dirty
/// activation); the real stream redelivers the event so the clean activation
/// applies the payload exactly once. Neither axis battery can produce it: the
/// persistence battery's stream is a dumb deliver-once reference that never
/// redelivers, and the streaming battery's store fault is a reference decorator,
/// not a real backend. Only both real backends at once close the conjunction.
/// </summary>
public abstract class WriteFaultRedeliveryScenarios<TFixture>
    where TFixture : PairingFixture
{
    readonly TFixture _fixture;

    protected WriteFaultRedeliveryScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EventConsumerWriteFault_DropsDirtyActivation_AndAppliesPayloadExactlyOnceOnRedelivery()
    {
        _fixture.StorageFault.Reset();
        var aggregateId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IStateWriteFaultPublisher>(aggregateId);
        var consumer = _fixture.GrainFactory.GetGrain<IStateWriteFaultConsumer>(aggregateId);
        var initialActivationId = await consumer.GetActivationIdAsync();

        var edictEvent = new StateWriteFaultEvent(aggregateId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        // Arrange — the commit write for this event's handling turn faults against
        // the real store.
        _fixture.StorageFault.ShouldFailWrites = true;
        await publisher.PublishAsync(edictEvent);

        // The handler mutates the payload, then the commit write faults out to the
        // stream layer, so the message is not acked.
        await PairingWaiters.WaitUntilAsync(() => Task.FromResult(_fixture.StorageFault.FailedWrites >= 1));
        Assert.True(_fixture.StorageFault.FailedWrites >= 1);

        // Heal the store. The framework dropped the dirty activation, so the
        // half-applied payload mutation is discarded; the changed activation id
        // confirms the reload from clean durable state.
        _fixture.StorageFault.ShouldFailWrites = false;
        await PairingWaiters.WaitUntilAsync(async () => await consumer.GetActivationIdAsync() != initialActivationId);
        Assert.NotEqual(initialActivationId, await consumer.GetActivationIdAsync());

        // The real stream redelivers; the clean activation applies the payload
        // exactly once — no double-apply on top of the half-applied turn.
        await PairingWaiters.WaitUntilAsync(async () => await consumer.GetAppliedCountAsync() == 1);
        Assert.Equal(1, await consumer.GetAppliedCountAsync());
    }
}
