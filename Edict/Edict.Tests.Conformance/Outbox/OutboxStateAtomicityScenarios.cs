using Xunit;

namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// The {State, Outbox} commit is a single atomic write to the framework-owned
/// grain state document. Proving it end-to-end on real grain storage: a forced
/// deactivate/reactivate round trip — where the persisted document is the only
/// thing that survives — must preserve the count mutation a previous command
/// applied, so a second command sees the persisted value (a volatile field
/// would reset to 0).
/// </summary>
public abstract class OutboxStateAtomicityScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected OutboxStateAtomicityScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task State_ShouldSurviveDeactivation_ProvingAtomicEnvelopeCommit()
    {
        var counterId = Guid.NewGuid();
        var aggregate = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        await _fixture.Sender.Send(new IncrementCounterCommand(counterId));
        Assert.Equal(1, await aggregate.GetCountAsync());

        await aggregate.DeactivateAsync();
        await Task.Delay(TimeSpan.FromSeconds(1)); // let the activation drain

        await _fixture.Sender.Send(new IncrementCounterCommand(counterId));

        Assert.Equal(2, await aggregate.GetCountAsync());

        var events = await CounterEventWaiters.WaitForEventsAsync(
            _fixture.GrainFactory, counterId, expectedCount: 2);
        Assert.Equal([1, 2], events.OfType<CounterIncrementedEvent>().Select(e => e.NewCount));
    }
}
