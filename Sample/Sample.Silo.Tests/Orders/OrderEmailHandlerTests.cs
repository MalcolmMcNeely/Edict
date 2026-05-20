using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Silo.Orders;

using Xunit;

namespace Sample.Silo.Tests.Orders;

/// <summary>
/// End-to-end through the shipped Test Framework: a single
/// <see cref="PlaceOrderCommand"/> runs the real engine across command →
/// raised event → <see cref="OrderEmailHandler"/> deferred invocation, and the
/// resulting workflow surfaces on one Verify-shaped <see cref="EdictTestApp.Timeline"/>
/// with the new <c>Invocation</c> kind alongside <c>Command</c> and
/// <c>Event</c> entries (issue #67 / ADR 0023).
/// </summary>
public sealed class OrderEmailHandlerTests
{
    [Fact]
    public async Task PlaceOrder_ShouldRecordInvocation_WhenOrderEmailHandlerHandlesOrderPlaced()
    {
        var orderId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId));
        await app.Drain();

        await Verify(app.Timeline);
    }
}
