using Edict.Azure.TableStorage;

namespace Edict.Azure.Tests.Projections;

/// <summary>
/// Azurite/Testcontainers conformance for the read-modify-write loop in
/// <c>EdictTableProjectionBuilder</c>: a second event loads the existing row
/// and writes back the delta. Lifted from <c>TableProjectionBuilderTests</c>
/// in Core.Tests.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class TableProjectionBuilderIncrementsOnSubsequentEventTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task HandleAsync_ShouldIncrementRowCount_WhenSubsequentEventArrives()
    {
        var orderId = Guid.NewGuid();
        var repository = new AzureTableRepository<AzureOrderTableRow>(
            fixture.TableServiceClient, "azureorderprojection");

        await fixture.Sender.Send(new AzurePlaceOrderCommand(orderId, "SKU-A"));
        await fixture.Sender.Send(new AzurePlaceOrderCommand(orderId, "SKU-B"));

        await WaitForRowAsync(repository, orderId.ToString(), orderId.ToString(),
            row => row.OrderCount >= 2);

        var row = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        await Verify(new { OrderCount = row!.OrderCount });
    }

    static async Task WaitForRowAsync(
        AzureTableRepository<AzureOrderTableRow> repository,
        string partitionKey,
        string rowKey,
        Func<AzureOrderTableRow, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(partitionKey, rowKey);
            if (row is not null && predicate(row))
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
