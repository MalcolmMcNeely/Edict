using Edict.Azure.TableStorage;

namespace Edict.Core.Tests.Grains;

/// <summary>
/// End-to-end test proving the Azure Table Storage write seam: grain writes via
/// <see cref="AzureTableWriteStoreFactory"/>, consumer reads back via
/// <see cref="AzureTableRepository{T}"/>. One test suffices to prove the Azure round-trip.
/// The full behaviour battery lives in <see cref="TableProjectionBuilderGrainTests"/>
/// against the in-memory fake (ADR 0015).
/// </summary>
[Collection(AzureTableE2ECollection.Name)]
public sealed class TableProjectionBuilderGrainAzureE2ETests(AzureTableE2EFixture fixture)
{
    [Fact]
    public async Task Event_delivery_writes_row_readable_via_azure_table_repository()
    {
        var orderId = Guid.NewGuid();
        var repository = new AzureTableRepository<OrderTableRow>(
            fixture.TableServiceClient, "orderprojection");

        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-E2E"));

        await WaitForRowAsync(repository, orderId.ToString(), orderId.ToString());
        var row = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        await Verify(new { OrderCount = row!.OrderCount })
            .UseParameters(orderId);
    }

    private static async Task WaitForRowAsync(
        AzureTableRepository<OrderTableRow> repository,
        string partitionKey,
        string rowKey)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(partitionKey, rowKey);
            if (row is not null && row.OrderCount >= 1)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
