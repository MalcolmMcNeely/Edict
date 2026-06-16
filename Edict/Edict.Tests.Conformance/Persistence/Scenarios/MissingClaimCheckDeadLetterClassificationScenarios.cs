using System.Diagnostics.Metrics;

using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Telemetry;
using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// A missing claim-check payload must dead-letter on two fronts: the
/// <c>edict.dead_letter.failure_reason</c> = <c>Substrate</c> metric (the real
/// store surfaces an absent payload as the typed
/// <c>EdictClaimCheckFetchException</c>, which the classifier buckets as
/// <c>Substrate</c>), and the persisted forensic row, which carries the
/// <c>BlobMissing</c> failure kind, the pointer envelope's <c>EventId</c> as the
/// parked-body locator, and the consumer grain key — promoted only after the
/// deferred fetch exhausts its retries, not on the first failure. Bound against a
/// fixture wiring the real claim-check store and an outbox tuned to dead-letter
/// quickly.
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
    public async Task MissingClaimCheck_ShouldDeadLetter_WithBlobMissingRowAndSubstrateClassification()
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
            InnerEventRouteKey = grainId.ToString("N"),
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

        // The promotion metric fires the moment exhaustion is detected, but the
        // forensic EdictDeadLetterRaised it stages is itself an outbox publish —
        // keep draining the consumer until that publish lands and the projection
        // upserts the row. The literal "deadletter" partition is shared across the
        // assembly, so the pointer envelope's unique EventId isolates this row.
        var deadLetterTable = _fixture.GetTableStore<EdictDeadLetterEntry>(EdictDeadLetterTable.Name);
        EdictDeadLetterEntry? deadLetterRow = null;
        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            await consumer.ForceDrainViaReminderAsync();
            var entries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
            deadLetterRow = entries.SingleOrDefault(entry => entry.SourceEventId == envelope.EventId);
            return deadLetterRow is not null;
        });

        Assert.NotNull(deadLetterRow);
        Assert.Equal(EdictDeadLetterFailureKind.BlobMissing, deadLetterRow.FailureKind);
        Assert.Equal(envelope.EventId, deadLetterRow.SourceEventId);
        Assert.Equal(grainId.ToString(), deadLetterRow.SourceGrainKey);
        // Promoted at the retry cap, not on the first failure.
        Assert.True(deadLetterRow.AttemptCount > 1);
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
