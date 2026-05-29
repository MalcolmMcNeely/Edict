using System.Diagnostics.Metrics;

using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Telemetry;
using Edict.Tests.Conformance.ClaimCheck;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Telemetry;

/// <summary>
/// Substrate-agnostic guarantee that two of the seven slice-1 instruments fire
/// on their intended event sites when an Edict silo runs against a real
/// substrate: <c>edict.claim_check.payload.size</c> records the inner-event
/// byte length on a payload-spilled raise, and
/// <c>edict.dead_letter.promotion.count</c> increments with the documented
/// allowlist failure-reason on a poisoned outbox entry. Bound against any
/// substrate's <see cref="ClaimCheckFixture"/> for the first scenario and any
/// fixture wiring a <see cref="ControllableOutboxExecutor"/> at
/// <c>OutboxMaxAttempts</c> = 2 for the second.
/// </summary>
public abstract class MetricsEmitOnExpectedEventsScenarios
{
}

public abstract class ClaimCheckPayloadSizeMetricsScenarios<TFixture>
    where TFixture : ClaimCheckFixture
{
    readonly TFixture _fixture;

    protected ClaimCheckPayloadSizeMetricsScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PayloadSizeMetric_ShouldFire_OnAPayloadSpilledRaise()
    {
        var counterId = Guid.NewGuid();
        var payload = new string('x', 64);

        var captures = new List<long>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName
                    && inst.Name == SemanticConventions.ClaimCheck.Meters.PayloadSize)
                {
                    l.EnableMeasurementEvents(inst);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            foreach (var t in tags)
            {
                if (t.Key == SemanticConventions.Events.Tags.ClaimChecked && (bool?)t.Value == true)
                {
                    lock (captures) { captures.Add(value); }
                    return;
                }
            }
        });
        listener.Start();

        await _fixture.Sender.Send(new IncrementClaimCheckCounterCommand(counterId, payload));

        // The publisher-side recording is synchronous with Send, so by the time
        // Send returns the histogram has already received the spilled-event
        // observation.
        long capturedValue;
        lock (captures)
        {
            Assert.NotEmpty(captures);
            capturedValue = captures[0];
        }
        Assert.True(capturedValue > 0, "spilled payload size must be > 0");
    }
}

public abstract class DeadLetterPromotionMetricsScenarios<TFixture>
    where TFixture : ConformanceFixture
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
        ControllableOutboxExecutor.Reset();
        ControllableOutboxExecutor.ShouldFail = true;

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

        await _fixture.Sender.Send(new IncrementCounterCommand(counterId));

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            await probe.ForceDrainViaReminderAsync();
            return ControllableOutboxExecutor.FailedAttempts >= 2;
        });

        // Heal so the promotion goes through the rest of the outbox path.
        ControllableOutboxExecutor.ShouldFail = false;

        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
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
        // The classifier maps the controllable's InvalidOperationException
        // to the Unhandled bucket — raw InvalidOperationException always
        // buckets there because no per-cause Edict* subtype matches it.
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.FailureReasonValues.Unhandled,
            capture.Tags[SemanticConventions.DeadLetter.Tags.FailureReason]);
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

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags);
}

/// <summary>
/// Substrate-agnostic guarantee that the silo-local
/// <c>edict.outbox.pending.count</c> observable gauge reports the
/// per-grain-type sum across multiple active aggregates. Bound
/// against any fixture wiring a <see cref="ControllableOutboxExecutor"/> at
/// <c>OutboxMaxAttempts</c> = 2: <c>ShouldFail</c> = true holds the published
/// events as failed-with-backoff entries in <c>Pending</c>, the OutboxHost's
/// post-write Report pushes the depth into the cache, the gauge's scrape
/// callback aggregates per grain type.
/// </summary>
public abstract class OutboxPendingCountMetricsScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected OutboxPendingCountMetricsScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PendingCountGauge_ShouldReportSumAcrossMultipleAggregatesOfTheSameType()
    {
        ControllableOutboxExecutor.Reset();
        ControllableOutboxExecutor.ShouldFail = true;

        try
        {
            var captures = new List<Capture>();
            using var listener = new MeterListener
            {
                InstrumentPublished = (inst, l) =>
                {
                    if (inst.Meter.Name == EdictDiagnostics.SourceName
                        && inst.Name == SemanticConventions.Outbox.Meters.PendingCount)
                    {
                        l.EnableMeasurementEvents(inst);
                    }
                },
            };
            listener.SetMeasurementEventCallback<int>((inst, value, tags, _) =>
            {
                var dict = new Dictionary<string, object?>(tags.Length);
                foreach (var t in tags) { dict[t.Key] = t.Value; }
                if ((dict.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string)?
                        .Contains("CounterAggregate") == true)
                {
                    lock (captures) { captures.Add(new Capture(value, dict)); }
                }
            });
            listener.Start();

            // Send across three distinct CounterAggregate keys — each grain
            // holds one pending publish entry under ShouldFail, so the
            // per-type sum the gauge sees should be 3.
            var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
            foreach (var id in ids)
            {
                // Send swallows the publish failure inside the inline drain so
                // the command still returns Accepted; the entry stays Pending
                // with a backed-off NextAttemptUtc.
                try { await _fixture.Sender.Send(new IncrementCounterCommand(id)); }
                catch { /* publish-side failure surfaces via the gauge, not the sender */ }
            }

            await WaitUntilAsync(() =>
            {
                listener.RecordObservableInstruments();
                lock (captures)
                {
                    return Task.FromResult(captures.Any(c => c.Value >= 3));
                }
            });

            int observedMax;
            lock (captures)
            {
                Assert.NotEmpty(captures);
                observedMax = captures.Max(c => c.Value);
            }
            Assert.True(observedMax >= 3,
                $"expected pending.count to reach at least 3 (one per CounterAggregate); observed max = {observedMax}");
        }
        finally
        {
            ControllableOutboxExecutor.ShouldFail = false;
        }
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition()) { return; }
            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }
    }

    sealed record Capture(int Value, IReadOnlyDictionary<string, object?> Tags);
}
