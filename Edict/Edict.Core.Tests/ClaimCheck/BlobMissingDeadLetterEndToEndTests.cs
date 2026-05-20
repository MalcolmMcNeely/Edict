using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Tests.Grains;

namespace Edict.Core.Tests.ClaimCheck;

/// <summary>
/// Receiver-side missing-blob loop, end-to-end (ADR 0024, slice 3): when a
/// consumer receives an <see cref="EdictEventEnvelope"/> whose claim-check
/// blob is not in the store (operator lifecycle policy reaped it), the
/// stream-observer machinery retries with exponential backoff bounded by
/// <c>EdictOutboxOptions.MaxAttempts</c> and on exhaustion promotes a
/// synthetic <c>EdictDeadLetterRaised</c> with
/// <see cref="EdictDeadLetterFailureKind.BlobMissing"/> into the same
/// fleet-wide forensic projection publisher-side failures use.
/// </summary>
[Collection(BlobMissingDeadLetterClusterCollection.Name)]
public sealed class BlobMissingDeadLetterEndToEndTests(BlobMissingDeadLetterClusterFixture fixture)
{
    [Fact]
    public async Task MissingBlob_ShouldDeadLetterAtMaxAttempts_WithBlobMissingFailureKindAndClaimCheckKey()
    {
        var grainId = Guid.NewGuid();
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IDedupTestConsumer>(grainId);

        // Pointer envelope to a key the store does NOT contain — every fetch
        // attempt throws KeyNotFoundException.
        var missingKey = $"edict-claim-check/{Guid.NewGuid():N}";
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: missingKey)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            InnerEventStreamName = "DedupTest",
            InnerEventRouteKey = grainId,
        };

        // MaxAttempts=3 on the fixture. Direct delivery through the same
        // OnEdictEventAsync the Orleans stream-callback uses; the observer
        // rethrows after persisting the bumped attempt counter on attempts
        // 1 and 2. The third invocation crosses MaxAttempts and promotes,
        // returning normally as the synthetic dead-letter publish entry is
        // drained inline.
        for (var i = 0; i < 3; i++)
        {
            try { await consumer.DeliverAsync(envelope); }
            catch (KeyNotFoundException) { }
            await Task.Delay(TimeSpan.FromMilliseconds(120));
        }

        var entry = await WaitForBlobMissingRowAsync(missingKey);
        Assert.NotNull(entry);
        Assert.Equal(EdictDeadLetterFailureKind.BlobMissing, entry.FailureKind);
        Assert.Equal(missingKey, entry.ClaimCheckKey);
        Assert.Equal(grainId.ToString(), entry.SourceGrainKey);
        Assert.Contains("DedupTestConsumer", entry.SourceGrainType);
        Assert.Equal("KeyNotFoundException", entry.ExceptionType);
        Assert.Contains(missingKey, entry.Reason ?? string.Empty);

        // The consumer's Handle was never invoked — the envelope never
        // materialised past the unwrap.
        var handled = await consumer.GetHandledEventIdsAsync();
        Assert.Empty(handled);
    }

    async Task<EdictDeadLetterEntry?> WaitForBlobMissingRowAsync(string key)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var rows = await fixture.DeadLetterRepository.ListAllAsync();
            var match = rows.FirstOrDefault(r => r.ClaimCheckKey == key);
            if (match is not null)
            {
                return match;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        return null;
    }
}
