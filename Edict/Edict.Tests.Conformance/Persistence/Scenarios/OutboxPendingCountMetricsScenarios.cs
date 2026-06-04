using System.Diagnostics.Metrics;

using Edict.Telemetry;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Substrate-agnostic guarantee that the silo-local
/// <c>edict.outbox.pending.count</c> observable gauge reports the
/// per-grain-type sum across multiple active aggregates against the real
/// persistence backend. Bound against a fixture wiring a
/// <see cref="ControllableOutboxExecutor"/> at <c>OutboxMaxAttempts</c> = 2:
/// <c>ShouldFail</c> = true holds the published events as failed-with-backoff
/// entries in <c>Pending</c>, the OutboxHost's post-write Report pushes the
/// depth into the cache, and the gauge's scrape callback aggregates per grain
/// type.
/// </summary>
public abstract class OutboxPendingCountMetricsScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected OutboxPendingCountMetricsScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PendingCountGauge_ShouldReportSumAcrossMultipleAggregatesOfTheSameType()
    {
        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.ShouldFail = true;

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

            // Send across three distinct CounterAggregate keys — each grain holds
            // one pending publish entry under ShouldFail, so the per-type sum the
            // gauge sees should be 3.
            var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
            foreach (var id in ids)
            {
                // Send swallows the publish failure inside the inline drain so the
                // command still returns Accepted; the entry stays Pending with a
                // backed-off NextAttemptUtc.
                try { await _fixture.Sender.SendAsync(new IncrementCounterCommand(id)); }
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
            _fixture.OutboxFault.ShouldFail = false;
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
