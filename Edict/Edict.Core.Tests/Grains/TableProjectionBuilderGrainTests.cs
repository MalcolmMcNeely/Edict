using Edict.Core.Tests.TableStorage;

namespace Edict.Core.Tests.Grains;

[Collection(EdictClusterCollection.Name)]
public sealed class TableProjectionBuilderGrainTests(EdictClusterFixture fixture)
{
    // Cycle 3 — tracer bullet: event delivery writes row; in-memory GetAsync returns it
    [Fact]
    public async Task Event_delivery_writes_row_via_in_memory_store()
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
    public async Task Consumer_specified_row_key_determines_row_coordinates()
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
    public async Task Global_singleton_stores_distinct_row_per_source_aggregate()
    {
        var orderIdA = Guid.NewGuid();
        var orderIdB = Guid.NewGuid();

        var publisher = fixture.Cluster.GrainFactory
            .GetGrain<IProjectionPublisherGrain>(GlobalOrderTableProjectionGrain.SingletonKey);

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

        var singletonPk = GlobalOrderTableProjectionGrain.SingletonKey.ToString();
        await WaitForPartitionCountAsync<OrderTableRow>("globalorderprojection", singletonPk, 2);
        var store = fixture.TableStoreFactory.GetStore<OrderTableRow>("globalorderprojection");
        var rows = store.GetPartition(singletonPk);

        await Verify(rows
            .OrderBy(r => r.OrderCount)
            .Select(r => new { r.OrderCount }));
    }

    // Cycle 4 — second event loads existing row and writes back delta
    [Fact]
    public async Task Second_event_loads_existing_row_and_increments_count()
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

    // Known limitation: table row write and dedup ring commit are non-atomic.
    // Double-apply on redelivery is accepted until the Outbox ships. See ADR 0012.
    [Fact(Skip = "Known limitation: accepted double-apply gap until the Outbox ships (ADR 0012).")]
    public Task Double_apply_on_crash_between_row_write_and_ring_commit_is_accepted_limitation()
        => Task.CompletedTask;

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
