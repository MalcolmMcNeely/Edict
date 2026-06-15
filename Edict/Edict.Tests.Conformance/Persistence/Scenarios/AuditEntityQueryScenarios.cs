using Edict.Contracts.Audit;
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
/// Persistence-axis conformance for the by-entity auditor query within a window: it
/// returns only the one aggregate's own records (its command decision then its raised
/// event) in chain order, never a peer aggregate's on the same correlation, and a
/// window that opens after the transaction excludes them.
/// </summary>
public abstract class AuditEntityQueryScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IAuditConformanceFixture
{
    readonly TFixture _fixture;

    protected AuditEntityQueryScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    static string OrderEntityType => typeof(OrderAggregate).FullName!;

    [Fact]
    public async Task ByEntity_ReturnsOneAggregatesHistoryWithinTheWindow_OrderedBySequence()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        // Act
        var accepted = await _fixture.Sender.SendAsync(new AuditPlaceOrderCommand(orderId));
        var correlationId = Assert.IsType<EdictCommandResult.Accepted>(accepted).Cursor.CorrelationId;
        await AuditConformanceWaiters.WaitForCorrelationRecordsAsync(_fixture, correlationId, expectedCount: 4);
        var after = DateTimeOffset.UtcNow;

        // Assert — only the order aggregate's own two records (its command decision
        // then its raised event), in chain order, never the stock aggregate's.
        var history = await _fixture.ByEntityInRangeAsync(OrderEntityType, orderId.ToString(), before, after);
        Assert.Equal([EdictAuditKind.Command, EdictAuditKind.Event], history.Select(record => record.Kind));
        Assert.Equal(
            [typeof(AuditPlaceOrderCommand).FullName, typeof(AuditOrderPlacedEvent).FullName],
            history.Select(record => record.MessageType));
        Assert.All(history, record => Assert.Equal(OrderEntityType, record.EntityType));

        // A window that opens after the transaction excludes its records.
        var afterWindow = await _fixture.ByEntityInRangeAsync(OrderEntityType, orderId.ToString(), after, after.AddMinutes(1));
        Assert.Empty(afterWindow);
    }
}
