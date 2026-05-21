using Edict.Azure.TableStorage;

namespace Edict.Azure.Tests.Resilience;

// KillSiloAsync (not StopSiloAsync) is used because graceful stop would wait
// for the in-flight handler to complete, defeating the "mid-HandleAsync"
// semantic.
[Collection(SiloKillCollection.Name)]
public sealed class SiloKilledMidHandlerTests(SiloKillClusterFixture fixture)
{
    [Fact]
    public async Task ProjectionHandle_ShouldWriteRowOnce_WhenHostingSiloKilledMidHandle()
    {
        SiloKillCoordinator.Reset();

        var aggregateId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<ISiloKillEventPublisher>(aggregateId);

        var evt = new SiloKillProjectionEvent(aggregateId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(evt);

        var hostingAddress = await SiloKillCoordinator.WaitForHandlerEnteredAsync(
            TimeSpan.FromSeconds(60));
        var hostingSilo = fixture.FindSiloByAddress(hostingAddress);

        await fixture.Cluster.KillSiloAsync(hostingSilo);
        await fixture.Cluster.RestartSiloAsync(hostingSilo);

        var repository = new AzureTableRepository<SiloKillTableRow>(
            fixture.TableServiceClient, SiloKillProjectionBuilder.Table);

        var row = await WaitForRowAsync(repository, aggregateId);

        Assert.NotNull(row);
        Assert.Equal(1, row!.Count);
        Assert.True(SiloKillCoordinator.HandlerEntries >= 2,
            $"Expected redelivery — saw only {SiloKillCoordinator.HandlerEntries} Handle entries.");
    }

    static async Task<SiloKillTableRow?> WaitForRowAsync(
        AzureTableRepository<SiloKillTableRow> repository,
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
