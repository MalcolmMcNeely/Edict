using System.Diagnostics.Metrics;

using Edict.Telemetry;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Substrate-agnostic guarantee that <c>edict.dead_letter.promotion.count</c>
/// increments with the documented allowlist failure-reason on a poisoned outbox
/// entry against the real persistence backend. Bound against a fixture wiring a
/// <see cref="ControllableOutboxExecutor"/> at <c>OutboxMaxAttempts</c> = 2.
/// </summary>
public abstract class DeadLetterPromotionMetricsScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected DeadLetterPromotionMetricsScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PromotionCounter_ShouldFire_OnPoisonedOutboxEntry_WithAllowlistFailureReason()
    {
        var counterId = Guid.NewGuid();
        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.ShouldFail = true;

        var captures = new List<Capture>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName
                    && inst.Name == SemanticConventions.DeadLetter.Meters.PromotionCount)
                {
                    l.EnableMeasurementEvents(inst);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            var dict = new Dictionary<string, object?>(tags.Length);
            foreach (var t in tags) { dict[t.Key] = t.Value; }
            // Multiple fixtures may share the test process — filter to this
            // counter's grain-type-prefixed grain key so peer scenarios don't
            // contaminate the capture.
            if ((dict.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string)?
                    .Contains("CounterAggregate") == true)
            {
                lock (captures) { captures.Add(new Capture(value, dict)); }
            }
        });
        listener.Start();

        await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            await probe.ForceDrainViaReminderAsync();
            return _fixture.OutboxFault.FailedAttempts >= 2;
        });

        // Heal so the promotion goes through the rest of the outbox path.
        _fixture.OutboxFault.ShouldFail = false;

        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            await probe.ForceDrainViaReminderAsync();
            lock (captures) { return captures.Count > 0; }
        });

        Capture capture;
        lock (captures)
        {
            Assert.NotEmpty(captures);
            capture = captures[0];
        }
        Assert.Equal(1L, capture.Value);
        Assert.Equal("PublishEvent", capture.Tags[SemanticConventions.Outbox.Tags.EffectKind]);
        // The classifier maps the controllable's InvalidOperationException to the
        // Unhandled bucket — raw InvalidOperationException always buckets there
        // because no per-cause Edict* subtype matches it.
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.FailureReasonValues.Unhandled,
            capture.Tags[SemanticConventions.DeadLetter.Tags.FailureReason]);
    }

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags);
}
