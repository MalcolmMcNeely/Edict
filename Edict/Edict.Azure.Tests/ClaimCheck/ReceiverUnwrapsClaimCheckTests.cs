namespace Edict.Azure.Tests.ClaimCheck;

/// <summary>
/// Receiver-side claim-check conformance against the real Azure stack
/// (ADR 0024 slice 3, ADR 0026): a pointer-bearing <c>EdictEventEnvelope</c>
/// arrives at an <c>EdictEventHandler</c> via the real Azure Queue Storage
/// stream. The consumer base's stream observer stages an
/// <c>OutboxEffectKind.InvokeHandler</c> entry; the engine's
/// <c>InvokeHandlerExecutor</c> fetches the body from
/// <see cref="Azure.ClaimCheck.AzureBlobClaimCheckStore"/> and dispatches
/// the unwrapped inner event to <c>Handle</c>. The test observes the handler
/// receiving the original event payload, end-to-end, with no
/// <c>EdictEventEnvelope</c> visible at the <c>Handle</c> seam.
/// </summary>
[Collection(AzureClaimCheckCollection.Name)]
public sealed class ReceiverUnwrapsClaimCheckTests(AzureClaimCheckClusterFixture fixture)
{
    [Fact]
    public async Task PointerEnvelope_ShouldUnwrapToInnerEvent_ForEdictEventHandler()
    {
        var counterId = Guid.NewGuid();
        var payload = $"unwrap-me-{Guid.NewGuid():N}";

        await fixture.Sender.Send(new IncrementAzureClaimCheckCounterCommand(counterId, payload));

        var handler = fixture.Cluster.GrainFactory.GetGrain<IAzureClaimCheckEventHandlerProbe>(counterId);
        var handled = await WaitForHandledAsync(handler);

        var inner = Assert.Single(handled);
        Assert.Equal(counterId, inner.CounterId);
        Assert.Equal(1, inner.NewCount);
        Assert.Equal(payload, inner.Payload);
    }

    static async Task<IReadOnlyList<AzureClaimCheckCounterIncrementedEvent>> WaitForHandledAsync(
        IAzureClaimCheckEventHandlerProbe handler, int expectedCount = 1, int timeoutSeconds = 30)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var events = await handler.GetHandledEventsAsync();
            if (events.Count >= expectedCount)
            {
                return events;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await handler.GetHandledEventsAsync();
    }
}
