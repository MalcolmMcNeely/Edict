using Edict.Tests.Conformance.Events;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance for the publish path: an accepted command raises an
/// event that lands on the bound streaming provider's domain stream with the
/// consumer-typed payload intact.
/// </summary>
public abstract class AcceptedCommandPublishesEventScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected AcceptedCommandPublishesEventScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AcceptedCommand_ShouldPublishEventToDomainStream()
    {
        var orderId = Guid.NewGuid();

        await _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, "SKU-1"));

        var events = await EventCaptureWaiters.WaitForEventsAsync(_fixture.GrainFactory, orderId);
        var placed = Assert.IsType<OrderPlacedEvent>(Assert.Single(events));
        Assert.Equal(orderId, placed.OrderId);
        Assert.Equal("SKU-1", placed.Sku);
    }
}
