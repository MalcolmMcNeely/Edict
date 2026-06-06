using Edict.Tests.Conformance.Projections;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis fidelity contract: a real stream provider must deliver a single
/// aggregate's events to its one projection activation <em>serially</em> — one
/// handler turn completing before the next begins — and count each exactly once.
/// A real Orleans pulling agent awaits each <c>OnNextAsync</c> before delivering
/// the next item from the same stream to the same consumer, so distinct events for
/// one activation are never in flight together. This is the production behaviour
/// the in-process Test Framework must mirror; binding it on every shipped streaming
/// provider keeps the contract provider-universal.
/// </summary>
public abstract class ProjectionDeliverySerializationScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected ProjectionDeliverySerializationScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Delivery_OfManyEventsToOneProjection_RunsSeriallyAndCountsExactly()
    {
        const int eventCount = 8;
        var orderId = Guid.NewGuid();

        // Send without awaiting settling between commands so the resulting events
        // burst onto the stream together — the realest chance the pulling agent
        // would have to deliver two of them to the one activation at once, were it
        // ever to do so.
        await Task.WhenAll(Enumerable.Range(0, eventCount).Select(_ =>
            _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, ConcurrencyProbe.Sku))));

        var projection = _fixture.GrainFactory.GetGrain<IOrderProjectionAccess>(orderId);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (await projection.GetOrderCountAsync() < eventCount && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        Assert.Equal(eventCount, await projection.GetOrderCountAsync());
        Assert.Equal(1, await projection.GetMaxConcurrentHandlerTurnsAsync());
    }
}
