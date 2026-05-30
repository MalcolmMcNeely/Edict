using Xunit;

namespace Edict.Tests.Conformance.Projections;

/// <summary>
/// Substrate-agnostic conformance for the read-modify-write loop in
/// <c>EdictTableProjectionBuilder</c>: a second event loads the existing row
/// and writes back the delta. Lifted from
/// <c>TableProjectionBuilderIncrementsOnSubsequentEventTests</c>.
/// </summary>
public abstract class TableProjectionIncrementsOnSubsequentEventScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected TableProjectionIncrementsOnSubsequentEventScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_ShouldIncrementRowCount_WhenSubsequentEventArrives()
    {
        var orderId = Guid.NewGuid();
        var repository = _fixture.GetTableRepository<OrderTableRow>("orderprojection");

        await _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, "SKU-A"));
        await _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, "SKU-B"));

        await TableProjectionWaiters.WaitForRowAsync(
            repository, orderId.ToString(), orderId.ToString(),
            row => row.OrderCount >= 2);

        var row = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        await Verify(new { OrderCount = row!.OrderCount });
    }
}
