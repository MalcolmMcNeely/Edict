using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Edict.Benchmarks.Throughput.Tests;

/// <summary>
/// Test-side raw <see cref="MeterListener"/> over the <c>"Npgsql"</c> meter,
/// capturing the instruments Npgsql 9.0.3 emits for the connection pool.
/// Used by the issue #148 probe to verify or falsify connection-pool pressure
/// as the cause of the kafkapostgres Commands-curve plateau.
/// <para>
/// Instrument names — the issue spec was drafted against the OpenTelemetry
/// semantic-conventions naming (<c>db.client.connections.*</c>, plural,
/// <c>usage</c>/<c>wait_time</c>), but Npgsql 9.0.3 publishes its own
/// pre-1.0 vendor-prefixed names:
/// <list type="bullet">
///   <item><c>db.client.connection.count</c> (singular, tagged
///     <c>state=idle|used</c>) — the OTel-spec "usage" gauge. Reported as an
///     <see cref="ObservableUpDownCounter{T}"/>, so each measurement is the
///     <em>current value</em>, not a delta. Observables only emit when
///     something calls <c>RecordObservableInstruments</c>; the probe drives
///     a periodic sample timer so the peak captures intra-window spikes.</item>
///   <item><c>db.client.connection.max</c> (singular) — pool ceiling, also
///     an observable.</item>
///   <item><c>db.client.connection.npgsql.pending_requests</c> — eager
///     <see cref="UpDownCounter{T}"/>. Npgsql only emits a delta when a
///     request actually has to wait; we sum deltas, watermark the running
///     total, and time the contiguous &gt;0 intervals so the verdict can
///     speak to "sustained &gt;1 s" against ADR-0029's threshold.</item>
///   <item><c>db.client.connection.npgsql.create_time</c> — histogram of
///     new-connection establishment cost in seconds. The closest signal to
///     the wait_time instrument the issue spec called for; we report p99
///     alongside the pending stat. Used as a secondary pressure proxy
///     (slow creates point at a flapping pool).</item>
/// </list>
/// </para>
/// </summary>
internal sealed class NpgsqlPoolListener : IDisposable
{
    readonly MeterListener _listener;
    readonly Lock _gate = new();
    readonly List<double> _createTimesSeconds = [];

    long _currentPending;
    long _peakPending;
    long _peakUsage;
    long _max;

    long _pendingStartedTicks;
    long _longestSustainedTicks;

    static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

    public NpgsqlPoolListener()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name != "Npgsql")
                {
                    return;
                }
                switch (instrument.Name)
                {
                    case "db.client.connection.npgsql.pending_requests":
                    case "db.client.connection.count":
                    case "db.client.connection.max":
                    case "db.client.connection.npgsql.create_time":
                        listener.EnableMeasurementEvents(instrument);
                        break;
                }
            },
        };
        _listener.SetMeasurementEventCallback<int>(OnIntMeasurement);
        _listener.SetMeasurementEventCallback<long>(OnLongMeasurement);
        _listener.SetMeasurementEventCallback<double>(OnDoubleMeasurement);
    }

    public void Start() => _listener.Start();

    public void RecordObservables() => _listener.RecordObservableInstruments();

    void OnIntMeasurement(Instrument instrument, int value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
        Record(instrument, value, tags);

    void OnLongMeasurement(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
        Record(instrument, value, tags);

    void OnDoubleMeasurement(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (instrument.Name != "db.client.connection.npgsql.create_time")
        {
            return;
        }
        lock (_gate)
        {
            _createTimesSeconds.Add(value);
        }
    }

    void Record(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        lock (_gate)
        {
            switch (instrument.Name)
            {
                case "db.client.connection.npgsql.pending_requests":
                    // Eager UpDownCounter — value is a delta. Maintain the
                    // running sum and watermark; track the longest
                    // contiguous >0 interval so the verdict can say
                    // "sustained pending for longer than 1 second" against
                    // the pool-pressure threshold.
                    var newPending = _currentPending + value;
                    if (newPending > 0 && _currentPending == 0)
                    {
                        _pendingStartedTicks = Stopwatch.ElapsedTicks;
                    }
                    else if (newPending == 0 && _currentPending > 0)
                    {
                        var ran = Stopwatch.ElapsedTicks - _pendingStartedTicks;
                        if (ran > _longestSustainedTicks)
                        {
                            _longestSustainedTicks = ran;
                        }
                        _pendingStartedTicks = 0;
                    }
                    _currentPending = newPending;
                    if (_currentPending > _peakPending)
                    {
                        _peakPending = _currentPending;
                    }
                    break;
                case "db.client.connection.count":
                    // ObservableUpDownCounter — value is the *current* count,
                    // not a delta. Npgsql 10 tags with
                    // db.client.connection.state ∈ {idle, used}. Pool pressure
                    // is about the "used" side approaching the cap.
                    var stateTag = TagValue(tags, "db.client.connection.state");
                    if (stateTag == "used" && value > _peakUsage)
                    {
                        _peakUsage = value;
                    }
                    break;
                case "db.client.connection.max":
                    // ObservableUpDownCounter — current cap. The pool only has
                    // one Max, so this is constant across samples; we keep the
                    // running max anyway in case the multi-pool case ever shows
                    // up (per-data-source pools each emit their own Max).
                    if (value > _max)
                    {
                        _max = value;
                    }
                    break;
            }
        }
    }

    static string? TagValue(ReadOnlySpan<KeyValuePair<string, object?>> tags, string name)
    {
        foreach (var kvp in tags)
        {
            if (kvp.Key == name)
            {
                return kvp.Value?.ToString();
            }
        }
        return null;
    }

    public Snapshot Capture()
    {
        lock (_gate)
        {
            // Close the in-flight pending run so a probe that exits while
            // pending is still nonzero still reports an honest sustained
            // duration. Without this the longest run only updates on the
            // pending → 0 transition, which may not happen before capture.
            var sustainedTicks = _longestSustainedTicks;
            if (_currentPending > 0)
            {
                var ran = Stopwatch.ElapsedTicks - _pendingStartedTicks;
                if (ran > sustainedTicks)
                {
                    sustainedTicks = ran;
                }
            }

            var createP99 = NearestRankPercentile(_createTimesSeconds, 0.99);

            return new Snapshot(
                PeakPendingRequests: _peakPending,
                PeakUsage: _peakUsage,
                MaxPoolSize: _max,
                CreateTimeP99Seconds: createP99,
                LongestSustainedPending: TimeSpan.FromSeconds(sustainedTicks / (double)System.Diagnostics.Stopwatch.Frequency),
                CreateTimeSampleCount: _createTimesSeconds.Count);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _currentPending = 0;
            _peakPending = 0;
            _peakUsage = 0;
            _max = 0;
            _createTimesSeconds.Clear();
            _pendingStartedTicks = 0;
            _longestSustainedTicks = 0;
        }
    }

    public void Dispose() => _listener.Dispose();

    static double NearestRankPercentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var rank = (int)Math.Ceiling(percentile * sorted.Length);
        if (rank < 1)
        {
            rank = 1;
        }
        if (rank > sorted.Length)
        {
            rank = sorted.Length;
        }
        return sorted[rank - 1];
    }

    public sealed record Snapshot(
        long PeakPendingRequests,
        long PeakUsage,
        long MaxPoolSize,
        double CreateTimeP99Seconds,
        TimeSpan LongestSustainedPending,
        int CreateTimeSampleCount);
}
