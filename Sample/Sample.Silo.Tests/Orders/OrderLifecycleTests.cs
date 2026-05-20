using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Silo.Orders;

using Xunit;

namespace Sample.Silo.Tests.Orders;

/// <summary>
/// Tracer bullet for the shipped Test Framework: a single command sent through
/// the in-memory <see cref="EdictTestApp"/> runs the real engine (route map,
/// inline outbox drain, memory streams) and the resulting Command + raised
/// Event surface on one Verify-shaped <see cref="EdictTestApp.Timeline"/>.
/// </summary>
public sealed class OrderLifecycleTests
{
    [Fact]
    public async Task PlaceOrder_ShouldRecordCommandAndRaisedEvent()
    {
        var orderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId));
        await app.Drain();

        await Verify(app.Timeline);
    }
}
