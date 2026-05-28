using System.Diagnostics.Metrics;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class NpgsqlPoolListenerTests
{
    [Fact]
    public void Capture_AccumulatesPendingDeltasAndObservableUsageHighWaterMark()
    {
        // Synthetic Meter named "Npgsql" mirrors the meter Npgsql 9 emits its
        // db.client.connections.* instruments on. The listener subscribes by
        // name, so we can drive it from a unit test without bringing up
        // Postgres. The aggregation differs by instrument shape:
        //   - pending_requests is an eager UpDownCounter (delta semantics)
        //     → sum and watermark
        //   - usage and max are ObservableUpDownCounters (current-value
        //     semantics) → replace and watermark, sampled via
        //     RecordObservables()
        // This test asserts both halves of that aggregation against a single
        // synthetic timeline.
        using var listener = new NpgsqlPoolListener();
        listener.Start();

        var observedUsage = 0;
        var observedMax = 100;
        using var meter = new Meter("Npgsql");
        var pending = meter.CreateUpDownCounter<int>("db.client.connection.npgsql.pending_requests");
        meter.CreateObservableUpDownCounter(
            "db.client.connection.count",
            () => new Measurement<int>(observedUsage, new KeyValuePair<string, object?>("db.client.connection.state", "used")));
        meter.CreateObservableUpDownCounter(
            "db.client.connection.max",
            () => observedMax);
        var createTime = meter.CreateHistogram<double>("db.client.connection.npgsql.create_time");

        // Pending climbs to 4 (peak), drains to 2.
        pending.Add(1);
        pending.Add(1);
        pending.Add(1);
        pending.Add(1);
        pending.Add(-1);
        pending.Add(-1);

        // Observable usage sweeps 3 → 7 → 5. Peak across sample points = 7.
        // RecordObservables() each time so the listener sees the current value.
        observedUsage = 3;
        listener.RecordObservables();
        observedUsage = 7;
        listener.RecordObservables();
        observedUsage = 5;
        listener.RecordObservables();

        // create_time samples — 100 measurements at 1 ms, one at 50 ms.
        // Nearest-rank p99 lands on index 98 of the sorted array (1 ms).
        for (var i = 0; i < 99; i++)
        {
            createTime.Record(0.001);
        }
        createTime.Record(0.050);

        var snapshot = listener.Capture();

        Assert.Equal(4, snapshot.PeakPendingRequests);
        Assert.Equal(7, snapshot.PeakUsage);
        Assert.Equal(100, snapshot.MaxPoolSize);
        Assert.Equal(0.001, snapshot.CreateTimeP99Seconds, precision: 6);
        Assert.Equal(100, snapshot.CreateTimeSampleCount);
    }
}
