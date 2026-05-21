namespace Edict.Azure.Tests.Events;

/// <summary>
/// Azurite/Testcontainers conformance for the publish-buffer drop on rejection:
/// a Rejected command discards its buffered events; nothing reaches the
/// stream. Lifted from <c>EventPublishingTests</c> in Core.Tests.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class RejectedCommandPublishesNoEventTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task RejectedCommand_ShouldPublishNoEvents()
    {
        var orderId = Guid.NewGuid();

        await fixture.Sender.Send(new AzureCancelOrderCommand(orderId, "changed mind"));

        await Task.Delay(TimeSpan.FromSeconds(3));
        var captureGrain = fixture.Cluster.GrainFactory.GetGrain<IAzureOrderEventCaptureGrain>(orderId);
        Assert.Empty(await captureGrain.GetCapturedEventsAsync());
    }
}
