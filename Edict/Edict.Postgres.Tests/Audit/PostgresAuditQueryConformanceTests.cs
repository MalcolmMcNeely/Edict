using System.Diagnostics;

using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Audit;

using Xunit;

namespace Edict.Postgres.Tests.Audit;

// Persistence-axis conformance for the auditor query surface on real Postgres,
// asserted against a correlation that genuinely spans two grains: placing an
// order captures on the OrderAggregate, the FulfilmentSaga turns the raised event
// into a stock reservation, and the StockAggregate captures under the same
// correlation and principal. From that one captured transaction the three queries
// reconstruct the cross-grain chain, the principal's timeline, and one aggregate's
// history.
[Collection(PostgresAuditCollection.Name)]
public sealed class PostgresAuditQueryConformanceTests(PostgresAuditFixture fixture)
{
    static string OrderEntityType => typeof(OrderAggregate).FullName!;
    static string StockEntityType => typeof(StockAggregate).FullName!;

    [Fact]
    public async Task ByCorrelation_ReconstructsTheChainAcrossEveryGrainItTouched_OrderedByIntentTime()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act — placing the order raises an event the saga turns into a stock
        // reservation on a second aggregate, so one correlation spans two grains.
        var accepted = await fixture.Sender.SendAsync(new PlaceOrderCommand(orderId));
        var correlationId = Assert.IsType<EdictCommandResult.Accepted>(accepted).Cursor.CorrelationId;

        var records = await WaitForCorrelationAsync(correlationId, expectedCount: 4);

        // Assert — four records, all on one correlation under one principal.
        Assert.Equal(4, records.Count);
        Assert.All(records, record => Assert.Equal(correlationId, record.CorrelationId));
        Assert.All(records, record => Assert.Equal(PostgresPersistenceFixtureBase.AuditPrincipal, record.Principal));

        // Returned in non-decreasing intent-time order.
        var occurredTimes = records.Select(record => record.OccurredAt).ToList();
        Assert.Equal(occurredTimes.OrderBy(time => time).ToList(), occurredTimes);

        // Reconstructed grain order: the order aggregate's records precede the
        // stock aggregate's, because the reservation only happens after the saga
        // reacts to the placed order.
        Assert.Equal(
            [OrderEntityType, OrderEntityType, StockEntityType, StockEntityType],
            records.Select(record => record.EntityType));

        // Each aggregate contributed its command decision and its one raised event.
        var orderMessages = records.Take(2).Select(record => record.MessageType).OrderBy(name => name, StringComparer.Ordinal);
        var stockMessages = records.Skip(2).Select(record => record.MessageType).OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(
            new[] { typeof(OrderPlacedEvent).FullName, typeof(PlaceOrderCommand).FullName }.OrderBy(name => name, StringComparer.Ordinal),
            orderMessages);
        Assert.Equal(
            new[] { typeof(ReserveStockCommand).FullName, typeof(StockReservedEvent).FullName }.OrderBy(name => name, StringComparer.Ordinal),
            stockMessages);
    }

    [Fact]
    public async Task ByPrincipal_ReturnsEverythingThePrincipalDidAcrossGrains_WithinTheWindow()
    {
        // Arrange — bound the window just before the transaction starts.
        var orderId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        // Act
        var accepted = await fixture.Sender.SendAsync(new PlaceOrderCommand(orderId));
        var correlationId = Assert.IsType<EdictCommandResult.Accepted>(accepted).Cursor.CorrelationId;
        await WaitForCorrelationAsync(correlationId, expectedCount: 4);
        var after = DateTimeOffset.UtcNow;

        // Assert — the principal's window holds the four records this transaction
        // captured across both aggregates, ordered by intent-time.
        var withinWindow = await fixture.ByPrincipalAsync(PostgresPersistenceFixtureBase.AuditPrincipal, before, after);
        Assert.Equal(4, withinWindow.Count);
        Assert.All(withinWindow, record => Assert.Equal(PostgresPersistenceFixtureBase.AuditPrincipal, record.Principal));
        Assert.All(withinWindow, record => Assert.Equal(correlationId, record.CorrelationId));

        var occurredTimes = withinWindow.Select(record => record.OccurredAt).ToList();
        Assert.Equal(occurredTimes.OrderBy(time => time).ToList(), occurredTimes);

        // A window that opens after the transaction excludes its records, so the
        // lower bound is honoured and the range is not the whole table.
        var afterWindow = await fixture.ByPrincipalAsync(PostgresPersistenceFixtureBase.AuditPrincipal, after, after.AddMinutes(1));
        Assert.DoesNotContain(afterWindow, record => record.CorrelationId == correlationId);
    }

    [Fact]
    public async Task ByEntity_ReturnsOneAggregatesHistoryWithinTheWindow_OrderedBySequence()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        // Act
        var accepted = await fixture.Sender.SendAsync(new PlaceOrderCommand(orderId));
        var correlationId = Assert.IsType<EdictCommandResult.Accepted>(accepted).Cursor.CorrelationId;
        await WaitForCorrelationAsync(correlationId, expectedCount: 4);
        var after = DateTimeOffset.UtcNow;

        // Assert — only the order aggregate's own two records (its command decision
        // then its raised event), in chain order, never the stock aggregate's.
        var history = await fixture.ByEntityInRangeAsync(OrderEntityType, orderId.ToString(), before, after);
        Assert.Equal([EdictAuditKind.Command, EdictAuditKind.Event], history.Select(record => record.Kind));
        Assert.Equal(
            [typeof(PlaceOrderCommand).FullName, typeof(OrderPlacedEvent).FullName],
            history.Select(record => record.MessageType));
        Assert.All(history, record => Assert.Equal(OrderEntityType, record.EntityType));

        // A window that opens after the transaction excludes its records.
        var afterWindow = await fixture.ByEntityInRangeAsync(OrderEntityType, orderId.ToString(), after, after.AddMinutes(1));
        Assert.Empty(afterWindow);
    }

    async Task<IReadOnlyList<EdictAuditRecord>> WaitForCorrelationAsync(Guid correlationId, int expectedCount)
    {
        IReadOnlyList<EdictAuditRecord> records = [];
        await WaitUntilAsync(async () =>
        {
            records = await fixture.ByCorrelationAsync(correlationId);
            return records.Count >= expectedCount;
        });
        Assert.True(records.Count >= expectedCount, $"Expected {expectedCount} audit records for correlation {correlationId}, saw {records.Count}.");
        return records;
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * 30);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(100);
        }
    }
}
