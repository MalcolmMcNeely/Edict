namespace Edict.Azure.Tests.Projections;

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
