using Edict.Contracts.ClaimCheck;

namespace Edict.Contracts.Tests.ClaimCheck;

public sealed class EnvelopeOverflowExceptionTests
{
    static readonly Guid RouteKey = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    const string EventType = "Sample.Orders.Events.OrderPlacedEvent";
    const int MeasuredBytes = 35_840;

    [Fact]
    public void Constructor_ShouldCarryRouteKeyEventTypeAndMeasuredByteLength()
    {
        var exception = new EdictEnvelopeOverflowException(RouteKey, EventType, MeasuredBytes);

        Assert.Equal(RouteKey, exception.RouteKey);
        Assert.Equal(EventType, exception.EventType);
        Assert.Equal(MeasuredBytes, exception.MeasuredBytes);
    }

    [Fact]
    public void Message_ShouldNameAllThreeForensicFields()
    {
        var exception = new EdictEnvelopeOverflowException(RouteKey, EventType, MeasuredBytes);

        Assert.Contains(EventType, exception.Message, StringComparison.Ordinal);
        Assert.Contains(MeasuredBytes.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(RouteKey.ToString(), exception.Message, StringComparison.Ordinal);
    }
}
