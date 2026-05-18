using Edict.Core.TableStorage;
using Edict.Core.Tests.Grains;

namespace Edict.Core.Tests.Grains;

[Collection(EdictClusterCollection.Name)]
public sealed class TableProjectionBuilderGrainTests(EdictClusterFixture fixture)
{
    // Cycle 1 — tracer bullet: event delivery writes row; ITableRepository point-get returns it
    [Fact]
    public async Task Event_delivery_writes_row_readable_via_point_get()
    {
        var orderId = Guid.NewGuid();
        var repository = new AzureTableRepository<OrderTableRow>(
            fixture.TableServiceClient, "orderprojection");

        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-1"));

        await WaitForTableRowAsync(repository, orderId.ToString(), orderId.ToString());
        var row = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        await Verify(row)
            .ScrubMember<OrderTableRow>(r => r.ETag)
            .ScrubMember<OrderTableRow>(r => r.Timestamp);
    }

    // Cycle 3 — RowKey is consumer-specified and independent of PartitionKey
    [Fact]
    public async Task Consumer_specified_row_key_determines_row_coordinates()
    {
        var orderId = Guid.NewGuid();
        var repository = new AzureTableRepository<OrderTableRow>(
            fixture.TableServiceClient, "ordersummary");

        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-C"));

        // PartitionKey = orderId (grain key), RowKey = "summary" (consumer-specified fixed key)
        await WaitForTableRowAsync(repository, orderId.ToString(), "summary");
        var row = await repository.GetAsync(orderId.ToString(), "summary");

        await Verify(new { row!.PartitionKey, row.RowKey, row.OrderCount })
            .UseParameters(orderId);
    }

    // Cycle 4 — global-singleton projection: fixed grain key, many aggregates → separate rows
    [Fact]
    public async Task Global_singleton_stores_distinct_row_per_source_aggregate()
    {
        var orderIdA = Guid.NewGuid();
        var orderIdB = Guid.NewGuid();
        var repository = new AzureTableRepository<OrderTableRow>(
            fixture.TableServiceClient, "globalorderprojection");

        // Publish directly to the singleton's stream key (bypassing CommandHandlerGrain routing)
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
        await WaitForPartitionRowCountAsync(repository, singletonPk, expectedRowCount: 2);
        var rows = await repository.QueryPartitionAsync(singletonPk);

        await Verify(rows
            .OrderBy(row => row.RowKey)
            .Select(row => new { row.RowKey, row.OrderCount }));
    }

    // Cycle 2 — partition query returns all rows written under the same PartitionKey
    [Fact]
    public async Task Partition_query_returns_all_rows_for_aggregate()
    {
        var orderId = Guid.NewGuid();
        var repository = new AzureTableRepository<OrderTableRow>(
            fixture.TableServiceClient, "orderprojection");

        // Two commands → two events → both increment the same row (same PK+RK)
        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-A"));
        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-B"));

        await WaitForTableRowAsync(repository, orderId.ToString(), orderId.ToString(),
            minimumCount: 2);

        var rows = await repository.QueryPartitionAsync(orderId.ToString());
        await Verify(rows.Select(row => new { row.PartitionKey, row.RowKey, row.OrderCount }));
    }

    // Cycle 5 — double-apply gap: knowingly accepted until the Outbox ships (ADR 0012).
    // A crash between WriteToTableAsync and Commit in EventDeduplicationGrain means the
    // event is redelivered and applied a second time. This is NOT exactly-once; do not
    // assert it as such. This test documents the known limitation rather than asserting
    // correct behaviour, so it is skipped until the Outbox is implemented.
    [Fact(Skip = "Known limitation: table row write and dedup ring commit are non-atomic. " +
                 "Double-apply on redelivery is accepted until the Outbox ships. See ADR 0012.")]
    public Task Double_apply_on_crash_between_row_write_and_ring_commit_is_accepted_limitation()
        => Task.CompletedTask;

    private static async Task WaitForPartitionRowCountAsync(
        AzureTableRepository<OrderTableRow> repository,
        string partitionKey,
        int expectedRowCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var rows = await repository.QueryPartitionAsync(partitionKey);
            if (rows.Count >= expectedRowCount)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private static async Task WaitForTableRowAsync(
        AzureTableRepository<OrderTableRow> repository,
        string partitionKey,
        string rowKey,
        int minimumCount = 1)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await repository.GetAsync(partitionKey, rowKey);
            if (row is not null && row.OrderCount >= minimumCount)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
