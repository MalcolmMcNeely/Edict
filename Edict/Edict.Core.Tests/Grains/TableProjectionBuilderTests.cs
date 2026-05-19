using Edict.Core.Tests.TableStorage;

namespace Edict.Core.Tests.Grains;

[Collection(EdictClusterCollection.Name)]
public sealed class TableProjectionBuilderTests(EdictClusterFixture fixture)
{
    // Cycle 3 — tracer bullet: event delivery writes row; in-memory GetAsync returns it
    [Fact]
    public async Task HandleAsync_ShouldWriteRowViaInMemoryStore_WhenEventIsDelivered()
    {
        var orderId = Guid.NewGuid();
        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-1"));

        await WaitForRowAsync<OrderTableRow>("orderprojection", orderId.ToString(), orderId.ToString());
        var store = fixture.TableStoreFactory.GetStore<OrderTableRow>("orderprojection");
        var row = store.Get(orderId.ToString(), orderId.ToString());

        await Verify(new { OrderCount = row!.OrderCount })
            .UseParameters(orderId);
    }

    // Cycle 3 — RowKey is consumer-specified and independent of PartitionKey
    [Fact]
    public async Task HandleAsync_ShouldUseConsumerSpecifiedRowKeyForRowCoordinates()
    {
        var orderId = Guid.NewGuid();
        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-C"));

        // PartitionKey = orderId (grain key), RowKey = "summary" (consumer-specified fixed key)
        await WaitForRowAsync<OrderTableRow>("ordersummary", orderId.ToString(), "summary");
        var store = fixture.TableStoreFactory.GetStore<OrderTableRow>("ordersummary");
        var row = store.Get(orderId.ToString(), "summary");

        await Verify(new { OrderCount = row!.OrderCount })
            .UseParameters(orderId);
    }

    // Cycle 3 — global-singleton projection: fixed grain key, many aggregates → separate rows
    [Fact]
    public async Task HandleAsync_ShouldStoreDistinctRowPerAggregate_WhenSingleton()
    {
        var orderIdA = Guid.NewGuid();
        var orderIdB = Guid.NewGuid();

        var publisher = fixture.Cluster.GrainFactory
            .GetGrain<IProjectionPublisherGrain>(GlobalOrderTableProjectionBuilder.SingletonKey);

        await publisher.PublishToStreamAsync("Orders", new OrderPlacedEvent(orderIdA, "SKU-X") with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await publisher.PublishToStreamAsync("Orders", new OrderPlacedEvent(orderIdB, "SKU-Y") with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var singletonPk = GlobalOrderTableProjectionBuilder.SingletonKey.ToString();
        await WaitForPartitionCountAsync<OrderTableRow>("globalorderprojection", singletonPk, 2);
        var store = fixture.TableStoreFactory.GetStore<OrderTableRow>("globalorderprojection");
        var rows = store.GetPartition(singletonPk);

        await Verify(rows
            .OrderBy(r => r.OrderCount)
            .Select(r => new { r.OrderCount }));
    }

    // Cycle 4 — second event loads existing row and writes back delta
    [Fact]
    public async Task HandleAsync_ShouldIncrementRowCount_WhenSubsequentEventArrives()
    {
        var orderId = Guid.NewGuid();
        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-A"));
        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-B"));

        await WaitForRowAsync<OrderTableRow>(
            "orderprojection",
            orderId.ToString(),
            orderId.ToString(),
            row => row.OrderCount >= 2);

        var store = fixture.TableStoreFactory.GetStore<OrderTableRow>("orderprojection");
        var row = store.Get(orderId.ToString(), orderId.ToString());

        await Verify(new { OrderCount = row!.OrderCount })
            .UseParameters(orderId);
    }

    // The former accepted double-apply gap (ADR 0012) is now closed by the
    // UpsertRow outbox effect; its mechanism proof lives in GapClosureTests and
    // the Azurite conformance proof in Edict.Azure.Tests (ADR 0018 / 0016).

    private async Task WaitForRowAsync<T>(
        string tableName,
        string partitionKey,
        string rowKey,
        Func<T, bool>? predicate = null)
        where T : class, new()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var store = fixture.TableStoreFactory.GetStore<T>(tableName);
                var row = store.Get(partitionKey, rowKey);
                if (row is not null && (predicate is null || predicate(row)))
                    return;
            }
            catch (KeyNotFoundException) { }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private async Task WaitForPartitionCountAsync<T>(
        string tableName,
        string partitionKey,
        int expectedCount)
        where T : class, new()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var store = fixture.TableStoreFactory.GetStore<T>(tableName);
                if (store.GetPartition(partitionKey).Count >= expectedCount)
                    return;
            }
            catch (KeyNotFoundException) { }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
