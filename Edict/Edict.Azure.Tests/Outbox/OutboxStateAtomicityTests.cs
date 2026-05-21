using Edict.Contracts.Events;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// The {State, Outbox} commit is a single atomic write to the
/// <c>edict-state</c> grain document. Proving it
/// end-to-end on Azurite: a forced deactivate/reactivate round trip — where
/// the persisted grain document is the only thing that survives — must
/// preserve the count mutation a previous command applied, so a second
/// command sees the persisted value (a volatile field would reset to 0).
/// Lifted from <c>OutboxEngineTests.State_ShouldSurviveDeactivation_ProvingAtomicEnvelopeCommit</c>;
/// now exercises real Azure Blob grain storage rather than in-memory.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class OutboxStateAtomicityTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task State_ShouldSurviveDeactivation_ProvingAtomicEnvelopeCommit()
    {
        var counterId = Guid.NewGuid();
        var aggregate = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterProbe>(counterId);

        await fixture.Sender.Send(new AzureIncrementCounterCommand(counterId));
        Assert.Equal(1, await aggregate.GetCountAsync());

        await aggregate.DeactivateAsync();
        await Task.Delay(TimeSpan.FromSeconds(1)); // let the activation drain

        await fixture.Sender.Send(new AzureIncrementCounterCommand(counterId));

        Assert.Equal(2, await aggregate.GetCountAsync());

        var events = await WaitForCounterEventsAsync(counterId, expectedCount: 2);
        Assert.Equal([1, 2], events.OfType<AzureCounterIncrementedEvent>().Select(e => e.NewCount));
    }

    async Task<IReadOnlyList<EdictEvent>> WaitForCounterEventsAsync(
        Guid counterId, int expectedCount)
    {
        var captureGrain = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterEventCaptureGrain>(counterId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var events = await captureGrain.GetCapturedEventsAsync();
            if (events.Count >= expectedCount)
            {
                return events;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await captureGrain.GetCapturedEventsAsync();
    }
}
