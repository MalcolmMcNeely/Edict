using Edict.Azure.TableStorage;

namespace Edict.Azure.Tests.Projections;

[Collection(AzureClusterCollection.Name)]
public sealed class TableProjectionBuilderConsumerRowKeyTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task HandleAsync_ShouldUseConsumerSpecifiedRowKeyForRowCoordinates()
    {
        var orderId = Guid.NewGuid();
        var repository = new AzureTableRepository<AzureOrderTableRow>(
            fixture.TableServiceClient, "azureordersummary");

        await fixture.Sender.Send(new AzurePlaceOrderCommand(orderId, "SKU-C"));

        await WaitForRowAsync(repository, orderId.ToString(), "summary");
        var row = await repository.GetAsync(orderId.ToString(), "summary");

        await Verify(new { OrderCount = row!.OrderCount });
    }

    static async Task WaitForRowAsync(
        AzureTableRepository<AzureOrderTableRow> repository,
        string partitionKey,
        string rowKey)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(partitionKey, rowKey);
            if (row is not null && row.OrderCount >= 1)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
