using Edict.Azure.TableStorage;

namespace Edict.Azure.Tests.Projections;

/// <summary>
/// End-to-end test proving the Azure Table Storage write seam: grain writes via
/// <see cref="AzureTableWriteStoreFactory"/>, consumer reads back via
/// <see cref="AzureTableRepository{T}"/>. One test suffices to prove the Azure round-trip.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class TableProjectionBuilderWritesRowTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task HandleAsync_ShouldWriteRowReadableViaAzureTableRepository()
    {
        var orderId = Guid.NewGuid();
        var repository = new AzureTableRepository<AzureOrderTableRow>(
            fixture.TableServiceClient, "azureorderprojection");

        await fixture.Sender.Send(new AzurePlaceOrderCommand(orderId, "SKU-E2E"));

        await WaitForRowAsync(repository, orderId.ToString(), orderId.ToString());
        var row = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        await Verify(new { OrderCount = row!.OrderCount })
            .UseParameters(orderId);
    }

    private static async Task WaitForRowAsync(
        AzureTableRepository<AzureOrderTableRow> repository,
        string partitionKey,
        string rowKey)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(partitionKey, rowKey);
            if (row is not null && row.OrderCount >= 1)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
