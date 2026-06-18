using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Audit;

using Xunit;

// The two-grain audit workload's PlaceOrderCommand / OrderPlacedEvent share a simple
// name with the single-grain ConformanceWorkload types in the enclosing namespace, so
// alias them to the saga-spanning pair this scenario needs.
using AuditOrderPlacedEvent = Edict.Tests.Conformance.Audit.OrderPlacedEvent;
using AuditPlaceOrderCommand = Edict.Tests.Conformance.Audit.PlaceOrderCommand;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Persistence-axis conformance for the by-correlation auditor query, asserted
/// against a correlation that genuinely spans two grains: placing an order captures
/// on the OrderAggregate, the FulfilmentSaga turns the raised event into a stock
/// reservation, and the StockAggregate captures under the same correlation and
/// principal. The query reconstructs the cross-grain chain in non-decreasing
/// intent-time order, with the order aggregate's records preceding the stock
/// aggregate's because the reservation only happens after the saga reacts.
/// </summary>
public abstract class AuditCorrelationQueryScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IAuditConformanceFixture
{
    readonly TFixture _fixture;

    protected AuditCorrelationQueryScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    static string OrderEntityType => typeof(OrderAggregate).FullName!;
    static string StockEntityType => typeof(StockAggregate).FullName!;

    [Fact]
    public async Task ByCorrelation_ReconstructsTheChainAcrossEveryGrainItTouched_OrderedByIntentTime()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act — placing the order raises an event the saga turns into a stock
        // reservation on a second aggregate, so one correlation spans two grains.
        var accepted = await _fixture.Sender.SendAsync(new AuditPlaceOrderCommand(orderId));
        var correlationId = Assert.IsType<EdictCommandResult.Accepted>(accepted).Cursor.ConversationId;

        var records = await AuditConformanceWaiters.WaitForCorrelationRecordsAsync(_fixture, correlationId, expectedCount: 4);

        // Assert — four records, all on one correlation under one principal.
        Assert.Equal(4, records.Count);
        Assert.All(records, record => Assert.Equal(correlationId, record.ConversationId));
        Assert.All(records, record => Assert.Equal(_fixture.AuditPrincipal, record.Principal));

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
            new[] { typeof(AuditOrderPlacedEvent).FullName, typeof(AuditPlaceOrderCommand).FullName }.OrderBy(name => name, StringComparer.Ordinal),
            orderMessages);
        Assert.Equal(
            new[] { typeof(ReserveStockCommand).FullName, typeof(StockReservedEvent).FullName }.OrderBy(name => name, StringComparer.Ordinal),
            stockMessages);
    }
}
