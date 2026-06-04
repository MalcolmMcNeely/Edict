namespace Edict.Kafka.Tests.Resilience;

// KillSiloAsync (not StopSiloAsync) is used because graceful stop would wait for
// the in-flight handler to complete, defeating the "mid-Handle" semantic. After
// the kill, RestartSiloAsync brings up a fresh silo on the same address; the new
// Edict.Kafka receiver re-Assigns its partition and reads from the consumer
// group's last committed offset — which is the offset BEFORE the in-flight event,
// because MessagesDeliveredAsync never ran. The EdictTableProjectionBuilder's
// atomic ring + UpsertRow commit guarantees the reference-store row settles at
// Count = 1 even though Handle entered the projection grain twice.
[Collection(KafkaSiloKillCollection.Name)]
public sealed class KafkaSiloKilledMidHandlerTests(KafkaSiloKillClusterFixture fixture)
{
    [Fact]
    public async Task ProjectionHandle_ShouldWriteRowOnce_WhenHostingSiloKilledMidHandle()
    {
        KafkaSiloKillCoordinator.Reset();

        var aggregateId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IKafkaSiloKillEventPublisher>(aggregateId);

        var edictEvent = new KafkaSiloKillEvent(aggregateId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(edictEvent);

        var hostingAddress = await KafkaSiloKillCoordinator.WaitForHandlerEnteredAsync(
            TimeSpan.FromSeconds(60));
        var hostingSilo = FindSiloByAddress(hostingAddress);

        await fixture.Cluster.KillSiloAsync(hostingSilo);
        await fixture.Cluster.RestartSiloAsync(hostingSilo);

        var row = await WaitForRowAsync(aggregateId, expectedCount: 1);

        Assert.NotNull(row);
        Assert.Equal(1, row!.Count);
        Assert.True(KafkaSiloKillCoordinator.HandlerEntries >= 2,
            $"Expected redelivery — saw only {KafkaSiloKillCoordinator.HandlerEntries} Handle entries.");
    }

    Orleans.TestingHost.SiloHandle FindSiloByAddress(Orleans.Runtime.SiloAddress address)
    {
        if (fixture.Cluster.Primary is { } primary && primary.SiloAddress.Equals(address))
        {
            return primary;
        }
        var match = fixture.Cluster.SecondarySilos.FirstOrDefault(
            s => s.SiloAddress.Equals(address));
        return match ?? throw new InvalidOperationException(
            $"No SiloHandle in the cluster matches address {address}.");
    }

    async Task<KafkaSiloKillTableRow?> WaitForRowAsync(Guid aggregateId, int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await fixture.GetProjectionRowAsync<KafkaSiloKillTableRow>(
                KafkaSiloKillProjectionBuilder.Table, aggregateId);
            if (row is not null && row.Count >= expectedCount)
            {
                return row;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        return await fixture.GetProjectionRowAsync<KafkaSiloKillTableRow>(
            KafkaSiloKillProjectionBuilder.Table, aggregateId);
    }
}
