using Edict.Contracts.Events;

namespace Edict.Azure.Tests.ClaimCheck;

/// <summary>
/// Publisher-side claim-check conformance against the real Azure stack
///: with a 1-byte <see cref="Core.ClaimCheck.ClaimCheckPolicy"/>
/// threshold, every raised event takes the pointer branch — the publisher
/// uploads the serialised inner event to the real Azurite blob via
/// <see cref="Azure.ClaimCheck.AzureBlobClaimCheckStore"/> and rides an
/// <see cref="EdictEventEnvelope"/> bearing a non-empty <c>ClaimCheckKey</c>
/// on the wire. A raw-Grain capture subscribed directly to the stream sees
/// the envelope (the Edict consumer base's unwrap is bypassed for raw
/// subscribers), letting this test pin the on-the-wire shape produced by
/// the publish side.
/// <para>
/// Lifted from <c>Edict.Core.Tests/ClaimCheck/</c>: the in-memory
/// <see cref="Testing.ClaimCheck.InMemoryClaimCheckStore"/> never proved the
/// real Azure Blob upload path, and the dead-letter/oversized variant in
/// Core.Tests largely re-proved shape already pinned by
/// <c>DeadLetterPromotionTests.BuildForEnvelopeFailure</c> and the
/// <c>HandlerFailurePromotesToDeadLetterTests</c> cluster proof against
/// Azurite (issue #89). This test isolates the publish-side decision against
/// real Azurite blob storage.
/// </para>
/// </summary>
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

        // Real Azurite blob carries the serialised inner event at the
        // advertised key — the operator forensic click-through from a
        // dead-letter row would resolve here in production.
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
