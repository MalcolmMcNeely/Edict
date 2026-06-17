using System.Diagnostics.Metrics;

using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Telemetry;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Proves the "safety net cannot throw" invariant end-to-end for each
/// <see cref="DeadLetterPromoter"/> degrade arm against the real persistence
/// backend. Each arm stages the poisoned outbox entry no consumer path produces,
/// drives the real engine through the existing reminder drain probe, and asserts
/// the promoter degrades to a synthetic dead-letter row carrying the marker
/// exception name, the outbox converges to empty (no poison-pill re-fire), and
/// <c>edict.dead_letter.promotion.failure.count</c> fires exactly once with the
/// bounded failure-reason tag. Bound against a fixture wiring the degrade-arm
/// failing executors at <c>OutboxMaxAttempts</c> = 2 with a minimal backoff, so
/// the drain converges through repeated probe ticks with no clock gate.
/// </summary>
public abstract class PromoterDegradeArmConvergenceScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected PromoterDegradeArmConvergenceScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public Task SerializationFailureArm_DegradesToSyntheticRow_AndCountsOnce() =>
        AssertDegradeArmConvergesAsync(
            probe => probe.StageUnserialisableForensicBodyEntryAsync(),
            expectedExceptionType: nameof(EdictPromotionSerializationException),
            expectedFailureReason: SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.SerializationFailure);

    [Fact]
    public Task UnsupportedKindArm_DegradesToSyntheticRow_AndCountsOnce() =>
        AssertDegradeArmConvergesAsync(
            probe => probe.StageUnsupportedKindEntryAsync(),
            expectedExceptionType: nameof(EdictUnsupportedEffectKindException),
            expectedFailureReason: SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.UnsupportedKind);

    [Fact]
    public Task MissingRouteKeyArm_DegradesToSyntheticRow_AndCountsOnce() =>
        AssertDegradeArmConvergesAsync(
            probe => probe.StageMissingRouteKeySendCommandEntryAsync(),
            expectedExceptionType: nameof(EdictMissingRouteKeyException),
            expectedFailureReason: SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.MissingRouteKey);

    async Task AssertDegradeArmConvergesAsync(
        Func<ICounterProbe, Task> stagePoisonedEntry,
        string expectedExceptionType,
        string expectedFailureReason)
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var captures = new List<Capture>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, enableEvents) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.DeadLetter.Meters.PromotionFailureCount)
                {
                    enableEvents.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var snapshot = new Dictionary<string, object?>(tags.Length);
            foreach (var tag in tags) { snapshot[tag.Key] = tag.Value; }
            if ((snapshot.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string)?
                    .Contains("CounterAggregate") == true)
            {
                lock (captures) { captures.Add(new Capture(value, snapshot)); }
            }
        });
        listener.Start();

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(sourceId);
        await stagePoisonedEntry(probe);

        // Act — drive the real drain through the existing reminder probe until the
        // promoted synthetic row has published and the outbox is empty. A tick's
        // grain round-trip outlasts the minimal backoff, so the failing entry is
        // re-ready each tick and the loop converges without a wall-clock gate.
        for (var tick = 0; tick < 12 && await probe.GetPendingOutboxCountAsync() > 0; tick++)
        {
            await probe.ForceDrainViaReminderAsync();
        }

        // Assert
        var deadLetterTable = _fixture.GetTableStore<EdictDeadLetterEntry>(EdictDeadLetterTable.Name);
        var row = await PromoterDegradeArmWaiters.WaitForDeadLetterRowAsync(deadLetterTable, sourceId.ToString("N"));

        Assert.Equal(expectedExceptionType, row.ExceptionType);
        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());

        Capture failure;
        lock (captures) { failure = Assert.Single(captures); }
        Assert.Equal(1L, failure.Value);
        Assert.Equal(
            expectedFailureReason,
            failure.Tags[SemanticConventions.DeadLetter.Tags.PromotionFailureReason]);
    }

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags);
}
