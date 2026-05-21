using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Tests.Grains;

namespace Edict.Core.Tests.ClaimCheck;

/// <summary>
/// Receiver-side missing-blob loop, end-to-end (ADR 0026, supersedes ADR 0024
/// slice 3): when a consumer receives an <see cref="EdictEventEnvelope"/>
/// whose claim-check blob is not in the store (operator lifecycle policy
/// reaped it), the stream-observer bifurcation stages an
/// <c>InvokeHandler</c> entry; the engine's per-entry retry calls
/// <c>ClaimCheckUnwrap</c>, fails the fetch, bumps backoff; on
/// <see cref="EdictOptions.OutboxMaxAttempts"/> exhaustion the standard
/// <c>IDeadLetterPromoter</c> path routes through the BlobMissing failure-kind
/// mapping into the same fleet-wide forensic projection publisher-side
/// failures use.
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
        // attempt the engine makes throws KeyNotFoundException.
        var missingKey = $"edict-claim-check/{Guid.NewGuid():N}";
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: missingKey)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            InnerEventStreamName = "DedupTest",
            InnerEventRouteKey = grainId,
        };

        // First delivery stages an InvokeHandler entry and runs the inline
        // drain (attempt #1, fails, bumped). The fixture's MaxAttempts is 3,
        // so we drive two more reminder ticks to exhaust retries — each fires
        // a fresh DrainAsync that re-attempts the gated-then-due entry.
        await consumer.DeliverAsync(envelope);
        await Task.Delay(TimeSpan.FromMilliseconds(120));
        await consumer.ForceDrainViaReminderAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(120));
        await consumer.ForceDrainViaReminderAsync();

        var entry = await WaitForBlobMissingRowAsync(missingKey);
        Assert.NotNull(entry);
        Assert.Equal(EdictDeadLetterFailureKind.BlobMissing, entry.FailureKind);
        Assert.Equal(missingKey, entry.ClaimCheckKey);
        Assert.Equal(grainId.ToString(), entry.SourceGrainKey);
        Assert.Contains("DedupTestConsumer", entry.SourceGrainType);
        Assert.Equal("System.Collections.Generic.KeyNotFoundException", entry.ExceptionType);
        Assert.Contains(missingKey, entry.Reason ?? string.Empty);

        // The consumer's Handle was never invoked — the envelope never
        // materialised past the executor's unwrap.
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
