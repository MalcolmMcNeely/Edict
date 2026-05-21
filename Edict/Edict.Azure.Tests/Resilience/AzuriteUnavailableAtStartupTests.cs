namespace Edict.Azure.Tests.Resilience;

/// <summary>
/// Scenario 3 of the transport-fault suite (issue #96): Azurite is unreachable
/// when the silo first tries to use it for an operation, then comes back.
/// Invariant: the silo retries and converges — the operation completes,
/// exactly once, against the now-available substrate.
///
/// The grain key is freshly minted per test so the consumer's first
/// activation has to hit Azure Blob grain storage to load its dedup state,
/// and the publisher's first publish has to hit Azure Queue Storage. With
/// Azurite paused, neither can make progress; once unpaused both reach
/// through and the event is delivered exactly once.
/// </summary>
[Collection(ResilienceCollection.Name)]
public sealed class AzuriteUnavailableAtStartupTests(ResilienceClusterFixture fixture)
{
    [Fact]
    public async Task PublishAndHandle_ShouldConverge_WhenAzuriteUnavailableAtFirstAttempt()
    {
        await fixture.EnsureRunningAsync();

        var aggregateId = Guid.NewGuid();
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IResilienceTestConsumer>(aggregateId);
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IResilienceEventPublisher>(aggregateId);

        var evt = new ResilienceTestEvent(aggregateId, 1) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        // Pause Azurite BEFORE the first substrate-touching operation, so the
        // silo's grain activation and stream publish both observe a downed
        // substrate at the moment they reach for it.
        await fixture.PauseAzuriteAsync();

        // Fire publish in the background — the call will hang inside the
        // grain's PublishAsync waiting on the Azure Queue write. Use a
        // detached task so the test can unpause while the publish is stuck.
        var publishTask = Task.Run(() => publisher.PublishEventAsync(evt));

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(publishTask.IsCompleted,
            "Publish should not complete while Azurite is paused.");

        await fixture.UnpauseAzuriteAsync();

        await publishTask.WaitAsync(TimeSpan.FromSeconds(60));

        var handled = await ResilienceWaiters.WaitForHandledAsync(consumer);
        Assert.Single(handled);
        Assert.Equal(evt.EventId, handled[0]);
    }
}
