using Edict.Testing;

using Sample.Contracts.Fulfillment.Commands;
using Sample.Domain.Fulfillment;
using Sample.Domain.Orders.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Fulfillment;

/// <summary>
/// Exercises <c>EdictTestAppBuilder.Replace&lt;IWarehouseGateway&gt;</c> against a
/// schedule fire handler: a recording <see cref="IWarehouseGateway"/> handed to
/// the builder must win on the silo container so each <c>FulfillNextLine</c> fire
/// routes its dispatch through the fake. Proves a schedule fire handler composes
/// with dependency injection through the same fake-injection seam an Event Handler
/// uses.
/// </summary>
public sealed class FulfillmentWarehouseGatewayReplaceTests
{
    [Fact]
    public async Task FireDueSchedules_ShouldDispatchEveryLine_ThroughTheReplacedGateway()
    {
        var orderId = Guid.Parse("f0000000-0000-0000-0000-000000000002");
        var lineItemIds = Enumerable.Range(0, 3)
            .Select(i => Guid.Parse($"f0000000-0000-0000-0000-00000000020{i}"))
            .ToArray();
        var gateway = new RecordingWarehouseGateway();

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .Replace<IWarehouseGateway>(gateway));

        await app.SendAsync(new StartFulfillmentCommand(orderId, lineItemIds));
        await app.Drain();

        for (var line = 0; line < lineItemIds.Length; line++)
        {
            await app.FireDueSchedulesAsync();
        }

        Assert.Equal(lineItemIds, gateway.DispatchedLineItemIds);
        Assert.All(gateway.DispatchedOrderIds, dispatchedOrderId => Assert.Equal(orderId, dispatchedOrderId));
    }

    sealed class RecordingWarehouseGateway : IWarehouseGateway
    {
        public List<Guid> DispatchedOrderIds { get; } = new();
        public List<Guid> DispatchedLineItemIds { get; } = new();

        public Task DispatchLineAsync(Guid orderId, Guid lineItemId)
        {
            DispatchedOrderIds.Add(orderId);
            DispatchedLineItemIds.Add(lineItemId);
            return Task.CompletedTask;
        }
    }
}
