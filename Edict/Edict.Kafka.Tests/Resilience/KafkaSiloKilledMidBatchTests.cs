using Edict.Postgres.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Kafka.Tests.Resilience;

// Mid-batch crash variant of KafkaSiloKilledMidHandlerTests: two events for
// the same aggregate are published back-to-back, so both land at the only
// receiver for the stream's single partition with their offsets uncommitted.
// The first delivery blocks inside Handle; the silo is killed before the
// pulling agent's MessagesDeliveredAsync fires. Because the Edict.Kafka
// receiver commits offsets only inside MessagesDeliveredAsync, neither
// event's offset has advanced — on restart the new consumer reads from the
// pre-batch offset and re-delivers BOTH events. Each one runs through Handle
// exactly once, so the projection row settles at Count = 2.
//
// What's being pinned: the EdictKafkaReceiver's commit-after-handle ordering,
// the enable.auto.commit=false floor, and the ring + UpsertRow atomic write
// — together they make a mid-batch crash safe for the tail of the batch, not
// just the in-flight event at the head.
[Collection(KafkaSiloKillCollection.Name)]
public sealed class KafkaSiloKilledMidBatchTests(KafkaSiloKillClusterFixture fixture)
{
    [Fact]
    public async Task ProjectionHandle_ShouldWriteRowOncePerEvent_WhenHostingSiloKilledMidBatch()
    {
        KafkaSiloKillBatchCoordinator.Reset();

        var aggregateId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory
            .GetGrain<IKafkaSiloKillBatchEventPublisher>(aggregateId);

        var first = new KafkaSiloKillBatchEvent(aggregateId, 1) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var second = new KafkaSiloKillBatchEvent(aggregateId, 2) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(first);
        await publisher.PublishAsync(second);

        var hostingAddress = await KafkaSiloKillBatchCoordinator.WaitForHandlerEnteredAsync(
            TimeSpan.FromSeconds(60));
        var hostingSilo = FindSiloByAddress(hostingAddress);

        await fixture.Cluster.KillSiloAsync(hostingSilo);
        await fixture.Cluster.RestartSiloAsync(hostingSilo);

        var serializer = fixture.Cluster.Client.ServiceProvider.GetRequiredService<Serializer>();
        var repository = new PostgresTableRepository<KafkaSiloKillTableRow>(
            fixture.PostgresConnectionString,
            KafkaSiloKillBatchProjectionBuilder.Table,
            serializer);

        var row = await WaitForRowAsync(repository, aggregateId, expectedCount: 2);

        Assert.NotNull(row);
        Assert.Equal(2, row!.Count);
        Assert.True(KafkaSiloKillBatchCoordinator.HandlerEntries >= 3,
            $"Expected redelivery of both events — saw only {KafkaSiloKillBatchCoordinator.HandlerEntries} Handle entries (initial first + both redelivered = ≥ 3).");
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

    static async Task<KafkaSiloKillTableRow?> WaitForRowAsync(
        PostgresTableRepository<KafkaSiloKillTableRow> repository,
        Guid aggregateId,
        int expectedCount)
    {
        var pk = aggregateId.ToString();
        var rk = aggregateId.ToString();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(pk, rk);
            if (row is not null && row.Count >= expectedCount)
            {
                return row;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        return await repository.GetAsync(pk, rk);
    }
}
