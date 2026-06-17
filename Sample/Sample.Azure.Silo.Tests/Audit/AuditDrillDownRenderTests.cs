using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Domain.Orders.CommandHandlers;
using Sample.Web.Components.Pages.Audit;

using Xunit;

namespace Sample.Azure.Silo.Tests.Audit;

/// <summary>
/// Consumer-facing coverage of the Sample audit drill-down's render path: the
/// page reads a captured body back as its concrete message through
/// <see cref="EdictTestApp.Audit"/>'s <c>GetMessageAsync</c> and renders the
/// decoded fields with <see cref="AuditMessageView"/>. Asserted only through the
/// consumer read surface and the sample's own render helper, never a framework internal.
/// </summary>
public sealed class AuditDrillDownRenderTests
{
    static string OrderEntityType => typeof(OrderCommandHandler).FullName!;

    [Fact]
    public async Task DrillDown_RendersTheDecodedCommandFields_ReadBackViaGetMessage()
    {
        // Arrange
        var orderId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithAudit());

        await app.SendAsync(new PlaceOrderCommand(orderId, "AUDIT-DEMO"));
        await app.Drain();

        var records = await app.Audit.ByEntityAsync(OrderEntityType, orderId.ToString("N"));
        var commandRecord = records.Single(record => record.Kind == EdictAuditKind.Command);

        // Act — read the captured body back as its concrete type, then render it the way the drill-down does.
        var message = await app.Audit.GetMessageAsync(commandRecord);
        var fields = AuditMessageView.Describe(message);

        // Assert
        await Verify(fields);
    }

    [Fact]
    public async Task DrillDown_RendersAHeterogeneousChain_ThroughOneDescribePath()
    {
        // Arrange
        var orderId = Guid.Parse("dddddddd-0000-0000-0000-000000000002");
        var lineItemId = Guid.Parse("dddddddd-0000-0000-0000-0000000000a1");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithAudit());

        // Act — drive an accepted order so the order grain's chain mixes command and
        // event types, then render every record through the single Describe path the
        // drill-down walks the chain with.
        await app.SendAsync(new PlaceOrderCommand(orderId, "AUDIT-DEMO"));
        await app.SendAsync(new AddLineItemCommand(orderId, lineItemId, "WIDGET", 1));
        await app.SendAsync(new SubmitOrderCommand(orderId, 100m));
        await app.Drain();

        var records = await app.Audit.ByEntityAsync(OrderEntityType, orderId.ToString("N"));

        var rendered = new List<object>();
        foreach (var record in records)
        {
            var message = await app.Audit.GetMessageAsync(record);
            rendered.Add(new { Decision = message.GetType().Name, record.Kind, Fields = AuditMessageView.Describe(message) });
        }

        // Assert
        await Verify(rendered);
    }

    [Fact]
    public async Task DrillDown_RendersEveryRecord_AcrossTheAcceptedCorrelationChain_WithDecodedFields()
    {
        // Arrange
        var orderId = Guid.Parse("dddddddd-0000-0000-0000-000000000003");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithAudit());

        // Act — drive the accepted flow the page's "place & submit" button drives, so the
        // submit correlation reconstructs the cross-grain chain the drill-down walks.
        await app.SendAsync(new PlaceOrderCommand(orderId, "AUDIT-DEMO"));
        await app.SendAsync(new AddLineItemCommand(orderId, Guid.NewGuid(), "WIDGET", 1));
        var submit = await app.SendAsync(new SubmitOrderCommand(orderId, 100m));
        var correlationId = Assert.IsType<EdictCommandResult.Accepted>(submit).Cursor.CorrelationId;
        await app.Drain();

        var records = await app.Audit.ByCorrelationAsync(correlationId);

        // Assert — every record on the chain decodes to a Sample message the drill-down
        // pattern-matches: none falls through to the bare type-name fallback. The failure
        // message names any type still left to cover.
        var uncovered = new List<string>();
        foreach (var record in records)
        {
            var message = await app.Audit.GetMessageAsync(record);
            var fields = AuditMessageView.Describe(message);
            if (fields is [{ Label: "Message" }])
            {
                uncovered.Add(message.GetType().Name);
            }
        }

        Assert.Empty(uncovered);
    }
}
