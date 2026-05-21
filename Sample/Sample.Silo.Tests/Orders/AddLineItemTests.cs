using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Silo.Orders.CommandHandlers;

using Xunit;

namespace Sample.Silo.Tests.Orders;

public sealed class AddLineItemTests
{
    [Fact]
    public async Task AddLineItem_ShouldRaiseEventCarryingTheCallerMintedLineItemId()
    {
        var orderId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var lineItemId = Guid.Parse("88888888-8888-8888-8888-888888888888");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, lineItemId, "SKU-1", 1));
        await app.Drain();

        await Verify(app.Timeline);
    }
}
