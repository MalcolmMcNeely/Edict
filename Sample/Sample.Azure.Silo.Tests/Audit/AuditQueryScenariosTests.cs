using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Domain.Orders.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Audit;

/// <summary>
/// Consumer-facing coverage of the audit query paths the Sample audit page is
/// built on: a rejected command is captured with its reasons and the actor who
/// tried, and one correlation reconstructs into a cross-grain chain ordered by
/// intent-time. Asserted only through the consumer read surface
/// (<see cref="EdictTestApp.Audit"/>), never a framework internal.
/// </summary>
public sealed class AuditQueryScenariosTests
{
    static string OrderEntityType => typeof(OrderCommandHandler).FullName!;

    [Fact]
    public async Task RejectedCommand_ShouldBeCaptured_WithItsReasonsAndTheActorWhoTried()
    {
        // Arrange
        var orderId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010");
        var auditor = EdictPrincipal.Of("auditor-9");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithAudit());

        // Act — submitting an order that was never placed has no line items, so the
        // handler rejects it; the decision is still captured, attributed to the actor.
        app.ActAs(auditor);
        var result = await app.SendAsync(new SubmitOrderCommand(orderId, 50m));
        await app.Drain();

        // Assert
        Assert.IsType<EdictCommandResult.Rejected>(result);

        var records = await app.Audit.ByEntityAsync(OrderEntityType, orderId.ToString("N"));
        var rejected = Assert.Single(records);
        Assert.Equal(EdictAuditKind.Command, rejected.Kind);
        Assert.Equal(EdictAuditOutcome.Rejected, rejected.Outcome);
        Assert.Equal(auditor, rejected.Principal);
        Assert.Contains(rejected.RejectionReasons, reason => reason.Code == "no_items");
    }

    [Fact]
    public async Task ByCorrelation_ShouldReconstructTheChainAcrossEveryGrain_OrderedByIntentTime()
    {
        // Arrange
        var orderId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
        var operatorPrincipal = EdictPrincipal.Of("ops-1");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithAudit());

        // Act — submitting under the decline threshold drives the payment saga, so
        // the submit correlation spans the order grain and the payment grain.
        app.ActAs(operatorPrincipal);
        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-011"));
        await app.SendAsync(new AddLineItemCommand(orderId, Guid.NewGuid(), "WIDGET", 1));
        var submit = await app.SendAsync(new SubmitOrderCommand(orderId, 100m));
        var correlationId = Assert.IsType<EdictCommandResult.Accepted>(submit).Cursor.CorrelationId;
        await app.Drain();

        // Assert — every record on the correlation, across more than one grain,
        // attributed to the same actor and returned in non-decreasing intent-time order.
        var records = await app.Audit.ByCorrelationAsync(correlationId);
        Assert.All(records, record => Assert.Equal(correlationId, record.CorrelationId));
        Assert.All(records, record => Assert.Equal(operatorPrincipal, record.Principal));

        var entityTypes = records.Select(record => record.EntityType).Distinct().ToList();
        Assert.True(entityTypes.Count >= 2, "The submit correlation should span more than one grain.");
        Assert.Contains(OrderEntityType, entityTypes);

        var occurredTimes = records.Select(record => record.OccurredAt).ToList();
        Assert.Equal(occurredTimes.OrderBy(time => time).ToList(), occurredTimes);
    }
}
