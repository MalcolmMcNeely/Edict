using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Telemetry;
using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// A missing claim-check payload must dead-letter with the
/// <c>edict.dead_letter.failure_reason</c> = <c>Substrate</c> classification: the
/// real store surfaces an absent payload as the typed
/// <c>EdictClaimCheckFetchException</c>, which the classifier buckets as
/// <c>Substrate</c>. Bound against a fixture wiring the real claim-check store
/// and an outbox tuned to dead-letter quickly.
/// </summary>
public abstract class MissingClaimCheckDeadLetterClassificationScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
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

        // A pointer envelope whose body the substrate's claim-check store does
        // not hold — every fetch attempt (by the envelope's EventId) raises
        // EdictClaimCheckFetchException, which the classifier maps to the
        // Substrate bucket. A never-written EventId is what isolates that path.
        var envelope = new EdictEventEnvelope(inlinePayload: null, eventId: Guid.NewGuid())
        {
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
