using Edict.Tests.Conformance.Projections;
using Edict.Tests.Conformance.Reactivation;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// The in-grain State projection's payload is framework-owned grain state, so it
/// must survive a deactivation: after the grain idles out and comes back from the
/// provider's real grain storage, a read answers with the total the events built.
/// The deactivation is driven through the <see cref="IConfirmsDeactivation"/> seam,
/// which completes only once the activation is genuinely torn down (a fresh
/// activation answers with a new id) — Orleans teardown is not gated by the injected
/// clock, so the seam drives a real reactivation rather than a wall-clock bridge
/// wait. The asserted property is the durable survival of the projected total across
/// reactivation, a function of the event set.
/// </summary>
public abstract class StateProjectionReactivationScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    const string Stream = "StateProjectionTotals";
    static readonly DateTimeOffset FixedOccurredAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    readonly TFixture _fixture;

    protected StateProjectionReactivationScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProjectedTotal_SurvivesDeactivation_AndReadsBack()
    {
        // Arrange — build a total, then make sure it has landed durably.
        var counterId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<IRunningTotalProjectionProbe>(counterId);

        await PublishAsync(counterId, amount: 5);
        await PublishAsync(counterId, amount: 7);
        await StateProjectionWaiters.WaitForTotalAsync(probe, expectedTotal: 12);

        // Act — force the projection to drop its in-memory state and come back from
        // the real grain store, so the read below can only be answered from durable state.
        await DeactivationWaiter.DeactivateAndConfirmAsync(probe);

        // Assert
        Assert.Equal(12, await probe.GetTotalAsync());
    }

    Task PublishAsync(Guid counterId, int amount) =>
        _fixture.GrainFactory.GetGrain<IStreamPublisher>(counterId).PublishAsync(
            Stream,
            new TotalIncrementedEvent(counterId, amount)
            {
                EventId = Guid.NewGuid(),
                OccurredAt = FixedOccurredAt,
            });
}
