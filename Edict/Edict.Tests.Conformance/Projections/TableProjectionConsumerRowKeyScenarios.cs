using Xunit;

namespace Edict.Tests.Conformance.Projections;

/// <summary>
/// Substrate-agnostic conformance proving a consumer-specified fixed RowKey
/// is honoured independent of PartitionKey. Lifted from
/// <c>TableProjectionBuilderConsumerRowKeyTests</c>.
/// </summary>
public abstract class TableProjectionConsumerRowKeyScenarios<TFixture>
    where TFixture : ConformanceFixture
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
        var repository = _fixture.GetTableRepository<OrderTableRow>("ordersummary");

        await _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, "SKU-C"));

        await TableProjectionWaiters.WaitForRowAsync(repository, orderId.ToString(), "summary");
        var row = await repository.GetAsync(orderId.ToString(), "summary");

        await Verify(new { OrderCount = row!.OrderCount });
    }
}
