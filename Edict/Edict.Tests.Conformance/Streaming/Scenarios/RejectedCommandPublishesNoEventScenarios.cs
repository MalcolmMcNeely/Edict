using Edict.Tests.Conformance.Events;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance for a Rejected command that raises nothing: a
/// handler can reject without publishing, so a rejection that raised no event
/// puts nothing on the stream. (A rejection that <em>does</em> raise publishes
/// the event — raising is the consumer's explicit intent, independent of the
/// result.)
/// </summary>
public abstract class RejectedCommandPublishesNoEventScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected RejectedCommandPublishesNoEventScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RejectedCommand_ShouldPublishNoEvents()
    {
        var orderId = Guid.NewGuid();

        await _fixture.Sender.SendAsync(new CancelOrderCommand(orderId, "changed mind"));

        // Sentinel-after: an accepted command on the same aggregate that DOES publish.
        // Both sends are awaited in order, so when the sentinel's event is captured the
        // rejected command has already been fully processed — and published nothing,
        // since the only captured event is the sentinel's.
        await _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, "SKU-sentinel"));

        var captureGrain = _fixture.GrainFactory.GetGrain<IOrderEventCaptureGrain>(orderId);
        await ConformanceWaiters.WaitUntilAsync(async () => (await captureGrain.GetCapturedEventsAsync()).Count >= 1);

        var captured = await captureGrain.GetCapturedEventsAsync();
        Assert.IsType<OrderPlacedEvent>(Assert.Single(captured));
    }
}
