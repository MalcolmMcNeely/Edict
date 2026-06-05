using Edict.Tests.Conformance.Projections;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Substrate-agnostic conformance for the read-modify-write loop in
/// <c>EdictListProjectionBuilder</c>: a second event loads the existing row
/// and writes back the delta.
/// </summary>
public abstract class TableProjectionIncrementsOnSubsequentEventScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
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
        var repository = _fixture.GetTableStore<OrderTableRow>("orderprojection");

        await _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, "SKU-A"));
        await _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, "SKU-B"));

        await TableProjectionWaiters.WaitForRowAsync(
            repository, orderId.ToString(), orderId.ToString(),
            row => row.OrderCount >= 2);

        var row = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        await Verify(new { OrderCount = row!.OrderCount });
    }
}
