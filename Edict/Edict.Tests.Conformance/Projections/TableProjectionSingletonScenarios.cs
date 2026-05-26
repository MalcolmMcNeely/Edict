using Xunit;

namespace Edict.Tests.Conformance.Projections;

/// <summary>
/// Substrate-agnostic conformance for a global-singleton table projection
/// (fixed grain key, many source aggregates → distinct rows under one
/// PartitionKey). Lifted from <c>TableProjectionBuilderSingletonTests</c>.
/// </summary>
public abstract class TableProjectionSingletonScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected TableProjectionSingletonScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_ShouldStoreDistinctRowPerAggregate_WhenSingleton()
    {
        var orderIdA = Guid.NewGuid();
        var orderIdB = Guid.NewGuid();

        var publisher = _fixture.GrainFactory
            .GetGrain<IStreamPublisher>(GlobalOrderTableProjectionBuilder.SingletonKey);

        await publisher.PublishAsync("ConformanceOrders", new OrderPlacedEvent(orderIdA, "SKU-X") with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await publisher.PublishAsync("ConformanceOrders", new OrderPlacedEvent(orderIdB, "SKU-Y") with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var singletonPk = GlobalOrderTableProjectionBuilder.SingletonKey.ToString();
        var repository = _fixture.GetTableRepository<OrderTableRow>("globalorderprojection");

        await TableProjectionWaiters.WaitForPartitionCountAsync(repository, singletonPk, 2);
        var rows = await repository.QueryPartitionAsync(singletonPk);

        await Verify(rows
            .OrderBy(r => r.OrderCount)
            .Select(r => new { r.OrderCount }));
    }
}
