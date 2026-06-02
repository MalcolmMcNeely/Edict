using Edict.Postgres.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Postgres.Tests.Resilience;

// Parity with the Azure and Kafka silo-kill tests, on the AQS-streams ×
// Postgres-grain-storage cross the Postgres provider suite owns. KillSiloAsync
// (not StopSiloAsync) tears the activation down mid-Handle, before the
// EdictTableProjectionBuilder's atomic dedup-ring + UpsertRow commit. The AQS
// message returns to visible after the visibility timeout and a surviving silo
// redelivers; the atomic commit guarantees the Postgres row settles at
// Count = 1 even though Handle entered the projection grain twice.
[Collection(PostgresSiloKillCollection.Name)]
public sealed class PostgresSiloKilledMidHandlerTests(PostgresSiloKillClusterFixture fixture)
{
    [Fact]
    public async Task ProjectionHandle_ShouldWriteRowOnce_WhenHostingSiloKilledMidHandle()
    {
        PostgresSiloKillCoordinator.Reset();

        var aggregateId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IPostgresSiloKillEventPublisher>(aggregateId);

        var edictEvent = new PostgresSiloKillProjectionEvent(aggregateId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(edictEvent);

        var hostingAddress = await PostgresSiloKillCoordinator.WaitForHandlerEnteredAsync(
            TimeSpan.FromSeconds(60));
        var hostingSilo = fixture.FindSiloByAddress(hostingAddress);

        await fixture.Cluster.KillSiloAsync(hostingSilo);
        await fixture.Cluster.RestartSiloAsync(hostingSilo);

        var serializer = fixture.Cluster.Client.ServiceProvider.GetRequiredService<Serializer>();
        var repository = new PostgresTableRepository<PostgresSiloKillTableRow>(
            fixture.PostgresDataSource,
            PostgresSiloKillProjectionBuilder.Table,
            serializer);

        var row = await WaitForRowAsync(repository, aggregateId);

        Assert.NotNull(row);
        Assert.Equal(1, row!.Count);
        Assert.True(PostgresSiloKillCoordinator.HandlerEntries >= 2,
            $"Expected redelivery — saw only {PostgresSiloKillCoordinator.HandlerEntries} Handle entries.");
    }

    static async Task<PostgresSiloKillTableRow?> WaitForRowAsync(
        PostgresTableRepository<PostgresSiloKillTableRow> repository,
        Guid aggregateId)
    {
        var pk = aggregateId.ToString();
        var rk = aggregateId.ToString();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(pk, rk);
            if (row is not null && row.Count > 0)
            {
                return row;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        return await repository.GetAsync(pk, rk);
    }
}
