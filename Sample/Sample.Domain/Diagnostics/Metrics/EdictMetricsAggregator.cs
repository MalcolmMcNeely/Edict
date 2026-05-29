using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

using Edict.Telemetry;

using Microsoft.Extensions.Hosting;

namespace Sample.Domain.Diagnostics.Metrics;

/// <summary>
/// Silo-process MeterListener that aggregates Edict's histogram and counter
/// samples into running stats the Live Metrics spoke can read. Started by the
/// silo host as an <see cref="IHostedService"/>; the probe grain reads from
/// the live instance per call.
/// <para>
/// Observable gauges (<c>edict.outbox.pending.count</c>,
/// <c>edict.outbox.oldest_entry.age</c>) are read straight from
/// <c>IEdictMetricsCache</c> by the probe grain — no listener needed for
/// those. This aggregator covers the histograms and counter:
/// <c>edict.event.handle.duration</c>, <c>edict.event.handle.lag</c>,
/// <c>edict.dead_letter.promotion.count</c>.
/// </para>
/// </summary>
public sealed class EdictMetricsAggregator : IHostedService, IDisposable
{
    static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(1);
    const int MaxSamplesPerHistogram = 4096;

    readonly TimeProvider _timeProvider;
    readonly ConcurrentQueue<Sample> _eventHandleDuration = new();
    readonly ConcurrentQueue<Sample> _eventHandleLag = new();
    readonly ConcurrentQueue<Sample> _deadLetterPromotions = new();
    MeterListener? _listener;

    public EdictMetricsAggregator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name != EdictDiagnostics.SourceName) { return; }
                switch (instrument.Name)
                {
                    case SemanticConventions.Events.Meters.HandleDuration:
                    case SemanticConventions.Events.Meters.HandleLag:
                    case SemanticConventions.DeadLetter.Meters.PromotionCount:
                        listener.EnableMeasurementEvents(instrument);
                        break;
                }
            },
        };
        _listener.SetMeasurementEventCallback<double>(OnDouble);
        _listener.SetMeasurementEventCallback<long>(OnLong);
        _listener.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Dispose();
        _listener = null;
        return Task.CompletedTask;
    }

    public void Dispose() => _listener?.Dispose();

    void OnDouble(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        var now = _timeProvider.GetUtcNow();
        var sample = new Sample(now, value);
        if (instrument.Name == SemanticConventions.Events.Meters.HandleDuration)
        {
            Append(_eventHandleDuration, sample, MaxSamplesPerHistogram, now);
        }
        else if (instrument.Name == SemanticConventions.Events.Meters.HandleLag)
        {
            Append(_eventHandleLag, sample, MaxSamplesPerHistogram, now);
        }
    }

    void OnLong(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        var now = _timeProvider.GetUtcNow();
        if (instrument.Name == SemanticConventions.DeadLetter.Meters.PromotionCount)
        {
            for (var i = 0; i < value; i++)
            {
                Append(_deadLetterPromotions, new Sample(now, 1), int.MaxValue, now);
            }
        }
    }

    static void Append(ConcurrentQueue<Sample> queue, Sample sample, int maxSamples, DateTimeOffset now)
    {
        queue.Enqueue(sample);
        var horizon = now - WindowDuration;
        while (queue.TryPeek(out var head) && (head.Timestamp < horizon || queue.Count > maxSamples))
        {
            queue.TryDequeue(out _);
        }
    }

    public double EventHandleDurationP99Seconds() => Percentile(_eventHandleDuration, 0.99);

    public double EventHandleLagP99Seconds() => Percentile(_eventHandleLag, 0.99);

    public double DeadLetterPromotionsPerSecond()
    {
        var now = _timeProvider.GetUtcNow();
        var horizon = now - WindowDuration;
        var count = 0;
        foreach (var sample in _deadLetterPromotions)
        {
            if (sample.Timestamp >= horizon) { count++; }
        }
        return count / WindowDuration.TotalSeconds;
    }

    static double Percentile(ConcurrentQueue<Sample> queue, double percentile)
    {
        var snapshot = queue.ToArray();
        if (snapshot.Length == 0) { return 0; }
        var values = new double[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++) { values[i] = snapshot[i].Value; }
        Array.Sort(values);
        var rank = (int)Math.Ceiling(percentile * values.Length);
        if (rank < 1) { rank = 1; }
        if (rank > values.Length) { rank = values.Length; }
        return values[rank - 1];
    }

    readonly record struct Sample(DateTimeOffset Timestamp, double Value);
}
