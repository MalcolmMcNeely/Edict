using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Audit;

using Xunit;

// Aliased to the two-grain audit workload's command, distinct from the single-grain
// ConformanceWorkload PlaceOrderCommand in the enclosing namespace.
using AuditPlaceOrderCommand = Edict.Tests.Conformance.Audit.PlaceOrderCommand;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Persistence-axis conformance for the by-principal auditor query: the principal's
/// window holds everything the principal did across grains in that range, ordered by
/// intent-time, and a window that opens after the transaction excludes its records so
/// the lower bound is honoured and the range is not the whole store.
/// </summary>
public abstract class AuditPrincipalQueryScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IAuditConformanceFixture
{
    readonly TFixture _fixture;

    protected AuditPrincipalQueryScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ByPrincipal_ReturnsEverythingThePrincipalDidAcrossGrains_WithinTheWindow()
    {
        // Arrange — bound the window just before the transaction starts.
        var orderId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        // Act
        var accepted = await _fixture.Sender.SendAsync(new AuditPlaceOrderCommand(orderId));
        var correlationId = Assert.IsType<EdictCommandResult.Accepted>(accepted).Cursor.CorrelationId;
        await AuditConformanceWaiters.WaitForCorrelationRecordsAsync(_fixture, correlationId, expectedCount: 4);
        var after = DateTimeOffset.UtcNow;

        // Assert — the principal's window holds the four records this transaction
        // captured across both aggregates, ordered by intent-time.
        var withinWindow = await _fixture.ByPrincipalAsync(_fixture.AuditPrincipal, before, after);
        Assert.Equal(4, withinWindow.Count);
        Assert.All(withinWindow, record => Assert.Equal(_fixture.AuditPrincipal, record.Principal));
        Assert.All(withinWindow, record => Assert.Equal(correlationId, record.CorrelationId));

        var occurredTimes = withinWindow.Select(record => record.OccurredAt).ToList();
        Assert.Equal(occurredTimes.OrderBy(time => time).ToList(), occurredTimes);

        // A window that opens after the transaction excludes its records, so the
        // lower bound is honoured and the range is not the whole store.
        var afterWindow = await _fixture.ByPrincipalAsync(_fixture.AuditPrincipal, after, after.AddMinutes(1));
        Assert.DoesNotContain(afterWindow, record => record.CorrelationId == correlationId);
    }
}
