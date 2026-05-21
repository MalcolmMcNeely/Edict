namespace Edict.Azure.Tests.Projections;

/// <summary>
/// Azurite/Testcontainers conformance for <c>EdictProjectionBuilder</c>
/// (ADR 0029): an accepted command's raised event is delivered to the
/// per-aggregate projection grain; an unhandled event on the same stream is
/// a pure no-op. Lifted from <c>ProjectionBuilderTests</c> in Core.Tests.
/// The publish→handle span stitch across the Azure Queue hop is proved once
/// by <c>EventHandlerSpanStitchAcrossOutboxHopTests</c>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class ProjectionBuilderTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task HandleAsync_ShouldDeliverEventToProjectionGrain_WhenCommandIsAccepted()
    {
        var orderId = Guid.NewGuid();

        await fixture.Sender.Send(new AzurePlaceOrderCommand(orderId, "SKU-1"));

        var projection = fixture.Cluster.GrainFactory.GetGrain<IAzureOrderProjectionAccess>(orderId);
        await WaitForProjectionAsync(projection, expectedCount: 1);
        Assert.Equal(1, await projection.GetOrderCountAsync());
    }

    [Fact]
    public async Task HandleAsync_ShouldBeNoOp_WhenEventTypeIsUnhandled()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureStreamPublisher>(grainId);
        var projection = fixture.Cluster.GrainFactory.GetGrain<IAzureOrderProjectionAccess>(grainId);

        // AzureUnknownOrderEvent has no Handle in AzureOrderProjectionBuilder
        // → DispatchAsync returns false; no-op.
        var unhandled = new AzureUnknownOrderEvent(grainId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync("AzureOrders", unhandled);

        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await projection.GetOrderCountAsync());
    }

    static async Task WaitForProjectionAsync(IAzureOrderProjectionAccess projection, int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await projection.GetOrderCountAsync() >= expectedCount)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
