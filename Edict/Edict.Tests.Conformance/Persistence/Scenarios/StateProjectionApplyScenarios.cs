using Edict.Tests.Conformance.Projections;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Binds the in-grain <strong>State projection species</strong>
/// (<c>EdictProjectionBuilder&lt;TProjection&gt;</c> carrying real payload state)
/// into conformance for the first time, over a real grain-state backend. The
/// projection accumulates each event's amount into its durable payload, so:
/// a single event advances the total; successive distinct events accumulate; and a
/// redelivery of the same <see cref="Edict.Contracts.Events.EdictEvent.EventId"/> is
/// suppressed by the dedup ring so the total advances exactly once — the
/// idempotent-re-apply invariant that guards the over-count bug class at the
/// projection level. Events are published directly to the stream with controlled
/// ids so the duplicate is deterministic rather than waiting on a transport to
/// redeliver, and a trailing distinct event acts as the sentinel that the duplicate
/// has been seen before the equality assertion runs. Every assertion is a function
/// of the event <em>set</em>, never its delivery order.
/// </summary>
public abstract class StateProjectionApplyScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    const string Stream = "StateProjectionTotals";
    static readonly DateTimeOffset FixedOccurredAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    readonly TFixture _fixture;

    protected StateProjectionApplyScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SingleEvent_AdvancesTheProjectedTotal()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<IRunningTotalProjectionProbe>(counterId);

        // Act
        await PublishAsync(counterId, Guid.NewGuid(), amount: 5);
        await StateProjectionWaiters.WaitForTotalAsync(probe, expectedTotal: 5);

        // Assert
        Assert.Equal(5, await probe.GetTotalAsync());
    }

    [Fact]
    public async Task DistinctEvents_AccumulateCumulatively()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<IRunningTotalProjectionProbe>(counterId);

        // Act
        await PublishAsync(counterId, Guid.NewGuid(), amount: 5);
        await PublishAsync(counterId, Guid.NewGuid(), amount: 7);
        await PublishAsync(counterId, Guid.NewGuid(), amount: 11);
        await StateProjectionWaiters.WaitForTotalAsync(probe, expectedTotal: 23);

        // Assert
        Assert.Equal(23, await probe.GetTotalAsync());
    }

    [Fact]
    public async Task SameEventIdRedelivery_AdvancesTheTotalExactlyOnce()
    {
        // Arrange
        var counterId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<IRunningTotalProjectionProbe>(counterId);
        var duplicatedEventId = Guid.NewGuid();

        // Act — publish the same EventId twice, then a distinct sentinel event. If the
        // dedup ring let the duplicate through the total would be 5 + 5 + 7 = 17; with
        // exactly-once re-apply it is 5 + 7 = 12.
        await PublishAsync(counterId, duplicatedEventId, amount: 5);
        await PublishAsync(counterId, duplicatedEventId, amount: 5);
        await PublishAsync(counterId, Guid.NewGuid(), amount: 7);
        await StateProjectionWaiters.WaitForTotalAsync(probe, expectedTotal: 12);

        // Assert
        Assert.Equal(12, await probe.GetTotalAsync());
    }

    Task PublishAsync(Guid counterId, Guid eventId, int amount) =>
        _fixture.GrainFactory.GetGrain<IStreamPublisher>(counterId).PublishAsync(
            Stream,
            new TotalIncrementedEvent(counterId, amount)
            {
                EventId = eventId,
                OccurredAt = FixedOccurredAt,
            });
}
