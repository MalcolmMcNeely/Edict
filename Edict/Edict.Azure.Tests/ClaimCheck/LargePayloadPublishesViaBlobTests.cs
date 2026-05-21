using Edict.Contracts.Events;

namespace Edict.Azure.Tests.ClaimCheck;

[Collection(AzureClaimCheckCollection.Name)]
public sealed class LargePayloadPublishesViaBlobTests(AzureClaimCheckClusterFixture fixture)
{
    [Fact]
    public async Task RaisedEvent_ShouldPublishAsPointerEnvelope_AndUploadBodyToBlob()
    {
        var counterId = Guid.NewGuid();
        var payload = new string('x', 64);

        await fixture.Sender.Send(new IncrementAzureClaimCheckCounterCommand(counterId, payload));

        var capture = fixture.Cluster.GrainFactory.GetGrain<IAzureClaimCheckEventCaptureGrain>(counterId);
        var captured = await WaitForCapturedAsync(capture);

        var envelope = Assert.IsType<EdictEventEnvelope>(Assert.Single(captured));
        Assert.False(string.IsNullOrEmpty(envelope.ClaimCheckKey),
            "publisher must populate ClaimCheckKey when ClaimCheckPolicy takes the pointer branch.");
        Assert.Null(envelope.InlinePayload);
        Assert.Equal("AzureClaimCheckCounters", envelope.InnerEventStreamName);
        Assert.Equal(counterId, envelope.InnerEventRouteKey);

        var container = fixture.BlobServiceClient.GetBlobContainerClient(fixture.ClaimCheckContainerName);
        var blob = container.GetBlobClient(envelope.ClaimCheckKey!);
        var exists = await blob.ExistsAsync();
        Assert.True(exists.Value, $"claim-check blob '{envelope.ClaimCheckKey}' must exist in container '{fixture.ClaimCheckContainerName}'.");
    }

    static async Task<IReadOnlyList<EdictEvent>> WaitForCapturedAsync(
        IAzureClaimCheckEventCaptureGrain capture, int timeoutSeconds = 15)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var events = await capture.GetCapturedEventsAsync();
            if (events.Count > 0)
            {
                return events;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await capture.GetCapturedEventsAsync();
    }
}
