using Xunit;

namespace Edict.Azure.Streaming.Tests.Resilience;

// KillSiloAsync (not StopSiloAsync) is used because graceful stop would wait for
// the in-flight handler to complete, defeating the "mid-HandleAsync" semantic. The
// kill lands before the projection's atomic ring + UpsertRow commit, so the first
// turn writes nothing; the AQS message returns to visible after the visibility
// timeout and a surviving silo redelivers, settling the reference-store row at
// Count = 1 even though Handle entered the projection grain twice.
[Collection(SiloKillCollection.Name)]
public sealed class SiloKilledMidHandlerTests(SiloKillClusterFixture fixture)
{
    [Fact]
    public async Task ProjectionHandle_ShouldWriteRowOnce_WhenHostingSiloKilledMidHandle()
    {
        SiloKillCoordinator.Reset();

        var aggregateId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<ISiloKillEventPublisher>(aggregateId);

        var edictEvent = new SiloKillProjectionEvent(aggregateId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(edictEvent);

        var hostingAddress = await SiloKillCoordinator.WaitForHandlerEnteredAsync(
            TimeSpan.FromSeconds(60));
        var hostingSilo = fixture.FindSiloByAddress(hostingAddress);

        await fixture.Cluster.KillSiloAsync(hostingSilo);
        await fixture.Cluster.RestartSiloAsync(hostingSilo);

        var row = await WaitForRowAsync(aggregateId);

        Assert.NotNull(row);
        Assert.Equal(1, row!.Count);
        Assert.True(SiloKillCoordinator.HandlerEntries >= 2,
            $"Expected redelivery — saw only {SiloKillCoordinator.HandlerEntries} Handle entries.");
    }

    async Task<SiloKillTableRow?> WaitForRowAsync(Guid aggregateId)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await fixture.GetProjectionRowAsync(aggregateId);
            if (row is not null && row.Count > 0)
            {
                return row;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        return await fixture.GetProjectionRowAsync(aggregateId);
    }
}
