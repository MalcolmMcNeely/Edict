using Edict.Tests.Conformance.Projections;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Substrate-agnostic conformance proving a consumer-specified fixed RowKey is
/// honoured independent of PartitionKey.
/// </summary>
public abstract class TableProjectionConsumerRowKeyScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected TableProjectionConsumerRowKeyScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_ShouldUseConsumerSpecifiedRowKeyForRowCoordinates()
    {
        var orderId = Guid.NewGuid();
        var repository = _fixture.GetTableStore<OrderTableRow>("ordersummary");

        await _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, "SKU-C"));

        await TableProjectionWaiters.WaitForRowAsync(repository, orderId.ToString("N"), "summary");
        var row = await repository.GetAsync(orderId.ToString("N"), "summary");

        await Verify(new { OrderCount = row!.OrderCount });
    }
}
