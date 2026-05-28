using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Routing;
using Edict.Core.Outbox;

namespace Edict.Core.Tests.Outbox;

[EdictStream("Orders")]
file sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

[EdictStream("Payments")]
file sealed partial record PaymentAuthorizedEvent(Guid PaymentId) : EdictEvent
{
    [EdictRouteKey]
    public Guid PaymentId { get; init; } = PaymentId;
}

[EdictStream("Unmapped")]
file sealed partial record UnmappedEvent(Guid Id) : EdictEvent
{
    [EdictRouteKey]
    public Guid Id { get; init; } = Id;
}

public class EventStreamAccessorsTests
{
    static EventStreamAccessors AccessorsFor(params (Type Type, EdictEventStreamAccessor Accessor)[] entries) =>
        new(entries.ToDictionary(e => e.Type, e => e.Accessor));

    [Fact]
    public void Resolve_ShouldReturnMappedStreamNameAndRouteKeyValue()
    {
        var orderId = Guid.NewGuid();
        var accessors = AccessorsFor(
            (typeof(OrderPlacedEvent),
                new EdictEventStreamAccessor("Orders", static evt => ((OrderPlacedEvent)evt).OrderId)));

        var (streamName, routeKey) = accessors.Resolve(new OrderPlacedEvent(orderId));

        Assert.Equal("Orders", streamName);
        Assert.Equal(orderId, routeKey);
    }

    [Fact]
    public void Resolve_ShouldDispatchOnConcreteType_WhenMapHoldsMultipleEntries()
    {
        var paymentId = Guid.NewGuid();
        var accessors = AccessorsFor(
            (typeof(OrderPlacedEvent),
                new EdictEventStreamAccessor("Orders", static evt => ((OrderPlacedEvent)evt).OrderId)),
            (typeof(PaymentAuthorizedEvent),
                new EdictEventStreamAccessor("Payments", static evt => ((PaymentAuthorizedEvent)evt).PaymentId)));

        var (streamName, routeKey) = accessors.Resolve(new PaymentAuthorizedEvent(paymentId));

        Assert.Equal("Payments", streamName);
        Assert.Equal(paymentId, routeKey);
    }

    [Fact]
    public void Resolve_ShouldThrow_WhenEventTypeIsUnregistered()
    {
        var accessors = AccessorsFor();

        var exception = Assert.Throws<InvalidOperationException>(
            () => accessors.Resolve(new UnmappedEvent(Guid.NewGuid())));

        Assert.Contains(nameof(UnmappedEvent), exception.Message, StringComparison.Ordinal);
    }
}
