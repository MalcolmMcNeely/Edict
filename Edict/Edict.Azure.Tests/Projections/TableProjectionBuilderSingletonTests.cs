using Edict.Azure.TableStorage;

namespace Edict.Azure.Tests.Projections;

/// <summary>
/// Azurite/Testcontainers conformance for a global-singleton table projection
/// (fixed grain key, many source aggregates → distinct rows under one
/// PartitionKey). Lifted from <c>TableProjectionBuilderTests</c> in Core.Tests.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class TableProjectionBuilderSingletonTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task HandleAsync_ShouldStoreDistinctRowPerAggregate_WhenSingleton()
    {
        var orderIdA = Guid.NewGuid();
        var orderIdB = Guid.NewGuid();

        var publisher = fixture.Cluster.GrainFactory
            .GetGrain<IAzureStreamPublisher>(AzureGlobalOrderTableProjectionBuilder.SingletonKey);

        await publisher.PublishAsync("AzureOrders", new AzureOrderPlacedEvent(orderIdA, "SKU-X") with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await publisher.PublishAsync("AzureOrders", new AzureOrderPlacedEvent(orderIdB, "SKU-Y") with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var singletonPk = AzureGlobalOrderTableProjectionBuilder.SingletonKey.ToString();
        var repository = new AzureTableRepository<AzureOrderTableRow>(
            fixture.TableServiceClient, "azureglobalorderprojection");

        await WaitForPartitionCountAsync(repository, singletonPk, 2);
        var rows = await repository.QueryPartitionAsync(singletonPk);

        await Verify(rows
            .OrderBy(r => r.OrderCount)
            .Select(r => new { r.OrderCount }));
    }

    static async Task WaitForPartitionCountAsync(
        AzureTableRepository<AzureOrderTableRow> repository,
        string partitionKey,
        int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var rows = await repository.QueryPartitionAsync(partitionKey);
            if (rows.Count >= expectedCount)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
