using Xunit;

namespace Edict.Tests.Conformance.Projections;

/// <summary>
/// End-to-end conformance proving the substrate's table-write seam: the
/// projection grain writes via <c>IEdictTableStoreFactory</c>, and the row is
/// readable back via the substrate's <c>IEdictTableRepository{T}</c>
/// implementation. Lifted from
/// <c>TableProjectionBuilderWritesRowTests.HandleAsync_ShouldWriteRowReadableViaAzureTableRepository</c>.
/// </summary>
public abstract class TableProjectionWritesRowScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected TableProjectionWritesRowScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_ShouldWriteRowReadableViaAzureTableRepository()
    {
        var orderId = Guid.NewGuid();
        var repository = _fixture.GetTableRepository<OrderTableRow>("orderprojection");

        await _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, "SKU-E2E"));

        await TableProjectionWaiters.WaitForRowAsync(repository, orderId.ToString(), orderId.ToString());
        var row = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        await Verify(new { OrderCount = row!.OrderCount })
            .UseParameters(orderId);
    }
}
