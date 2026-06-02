using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Telemetry;

using Xunit;

namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// Cross-substrate guarantee that a missing claim-check payload dead-letters
/// with the same <c>edict.dead_letter.failure_reason</c> classification
/// regardless of backend. Both stores surface an absent payload as the typed
/// <c>EdictClaimCheckFetchException</c>, so the promotion counter must tag the
/// failure as <c>Substrate</c> on every substrate. Bound against any fixture
/// that wires the substrate's <c>IEdictClaimCheckStore</c> and tunes the outbox
/// to dead-letter quickly.
/// </summary>
public abstract class MissingClaimCheckDeadLetterClassificationScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected MissingClaimCheckDeadLetterClassificationScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MissingClaimCheck_ShouldDeadLetter_WithSubstrateFailureReason()
    {
        var grainId = Guid.NewGuid();
        var consumer = _fixture.GrainFactory.GetGrain<IClaimCheckBlobMissingConsumer>(grainId);

        // A pointer envelope whose payload the substrate's claim-check store
        // does not hold — every fetch attempt raises EdictClaimCheckFetchException
        // (PayloadMissing), which the classifier maps to the Substrate bucket.
        // The key is a bare GUID-N so it is well-formed for both stores (the
        // Postgres store rejects a non-GUID key as KeyMalformed before lookup);
        // a well-formed-but-absent key is what isolates the PayloadMissing path.
        var missingKey = Guid.NewGuid().ToString("N");
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: missingKey)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            InnerEventStreamName = "ConformanceClaimCheckBlobMissing",
            InnerEventRouteKey = grainId,
        };

        var captures = new List<string?>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, enabledListener) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.DeadLetter.Meters.PromotionCount)
                {
                    enabledListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var dictionary = new Dictionary<string, object?>(tags.Length);
            foreach (var tag in tags)
            {
                dictionary[tag.Key] = tag.Value;
            }
            // Peer scenarios share the test process — isolate to this probe's
            // grain type so their promotions do not contaminate the capture.
            if ((dictionary.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string)?
                    .Contains(nameof(ClaimCheckBlobMissingConsumer)) == true)
            {
                lock (captures)
                {
                    captures.Add(dictionary.GetValueOrDefault(SemanticConventions.DeadLetter.Tags.FailureReason) as string);
                }
            }
        });
        listener.Start();

        await consumer.DeliverAsync(envelope);

        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            await consumer.ForceDrainViaReminderAsync();
            lock (captures)
            {
                return captures.Count > 0;
            }
        });

        string? failureReason;
        lock (captures)
        {
            Assert.NotEmpty(captures);
            failureReason = captures[0];
        }
        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, failureReason);
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }
    }
}
