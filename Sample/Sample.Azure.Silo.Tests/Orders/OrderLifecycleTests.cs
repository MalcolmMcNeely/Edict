using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Events;
using Sample.Domain.Orders.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Orders;

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

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Drain();

        await Verify(app.Timeline);
    }

    [Fact]
    public async Task PlaceOrder_AndAddLineItem_ShouldCarryCallerMintedLineItemId()
    {
        var orderId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var lineItemId = Guid.Parse("88888888-8888-8888-8888-888888888888");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, lineItemId, "SKU-1", 1));
        await app.Drain();

        // Pin only the LineItemAddedEvent payload shape — full-timeline ordering is
        // chaos-sensitive (Invocation entries from OrderEmailEventHandler can land
        // between command and the next event depending on cascade timing).
        var lineItemEvents = app.Timeline.Entries
            .Where(e => e.Kind == "Event" && e.Type == nameof(LineItemAddedEvent))
            .ToList();

        await Verify(lineItemEvents);
    }
}
