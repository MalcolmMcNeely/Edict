using System.Collections.Immutable;

using Edict.Benchmarks.Throughput.ClosedLoop;
using Edict.Benchmarks.Throughput.Measurement;
using Edict.Benchmarks.Throughput.Output;
using Edict.Benchmarks.Throughput.Saturation;

using static VerifyXunit.Verifier;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class SubstrateSummaryStoreTests
{
    [Fact]
    public async Task RoundTrip_PreservesClosedLoopAndSaturationFields()
    {
        var runDate = new DateTimeOffset(2026, 5, 29, 11, 22, 33, TimeSpan.Zero);
        var summary = SubstrateSummaryStore.BuildFromResults(
            substrate: "kafkapostgres",
            runDate: runDate,
            closedLoop:
            [
                new ThroughputResults(
                    Substrate: "kafkapostgres",
                    Scenario: "Command acceptance",
                    Parallelism: 16,
                    CompletedCount: 67_412,
                    ElapsedMeasurement: TimeSpan.FromSeconds(30),
                    Latency: new LatencyResults(
                        P50: TimeSpan.FromMilliseconds(2.94),
                        P95: TimeSpan.FromMilliseconds(4.27),
                        P99: TimeSpan.FromMilliseconds(5.82)),
                    Health: new RunHealth(
                        Succeeded: 67_412,
                        Failed: 0,
                        FailuresByType: ImmutableSortedDictionary<string, long>.Empty)),
            ],
            saturation: new SaturationResults(
                Substrate: "kafkapostgres",
                EventsPerSecond: 373.13,
                WindowSeconds: 30,
                ProducerConcurrency: 256,
                AggregateCount: 1024,
                Health: new RunHealth(
                    Succeeded: 11_188,
                    Failed: 0,
                    FailuresByType: ImmutableSortedDictionary<string, long>.Empty)));

        var path = Path.Combine(Path.GetTempPath(), $"edict-summary-roundtrip-{Guid.NewGuid():N}.json");
        try
        {
            await SubstrateSummaryStore.WriteAsync(path, summary);
            var read = await SubstrateSummaryStore.ReadAsync(path);

            await Verify(read);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ReadAllAsync_UnionsEverySubstrateSummary()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"edict-summary-readall-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await SubstrateSummaryStore.WriteAsync(
                SubstrateSummaryStore.PathFor(directory, "azure"),
                new SubstrateSummary
                {
                    Substrate = "azure",
                    RunDate = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero),
                });
            await SubstrateSummaryStore.WriteAsync(
                SubstrateSummaryStore.PathFor(directory, "kafkapostgres"),
                new SubstrateSummary
                {
                    Substrate = "kafkapostgres",
                    RunDate = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero),
                });

            var summaries = await SubstrateSummaryStore.ReadAllAsync(directory);

            Assert.Equal(2, summaries.Count);
            Assert.Contains(summaries, s => s.Substrate == "azure");
            Assert.Contains(summaries, s => s.Substrate == "kafkapostgres");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadAllAsync_OnMissingDirectory_ReturnsEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"edict-summary-absent-{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(directory));

        var summaries = await SubstrateSummaryStore.ReadAllAsync(directory);

        Assert.Empty(summaries);
    }

    [Fact]
    public Task Hydrate_RebuildsResultsAndRunDates_FromSummaries()
    {
        var summaries = new[]
        {
            new SubstrateSummary
            {
                Substrate = "azure",
                RunDate = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero),
                ClosedLoop =
                [
                    new ClosedLoopSummaryRow
                    {
                        Scenario = "Command acceptance",
                        Parallelism = 2,
                        P50Ms = 11.95,
                        P95Ms = 18.56,
                        P99Ms = 25.52,
                        Health = new RunHealthSummary { Succeeded = 100 },
                    },
                ],
                Saturation = new SaturationSummaryRow
                {
                    EventsPerSecond = 70,
                    WindowSeconds = 30,
                    ProducerConcurrency = 256,
                    AggregateCount = 1024,
                    Health = new RunHealthSummary { Succeeded = 2100 },
                },
            },
            new SubstrateSummary
            {
                Substrate = "kafkapostgres",
                RunDate = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero),
            },
        };

        var hydrated = SubstrateSummaryStore.Hydrate(summaries);

        return Verify(hydrated);
    }

    [Fact]
    public async Task WriteAsync_OverwritesPriorSummaryForSameSubstrate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"edict-summary-overwrite-{Guid.NewGuid():N}.json");
        try
        {
            await SubstrateSummaryStore.WriteAsync(path, new SubstrateSummary
            {
                Substrate = "kafkapostgres",
                RunDate = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero),
                Saturation = new SaturationSummaryRow { EventsPerSecond = 210 },
            });
            await SubstrateSummaryStore.WriteAsync(path, new SubstrateSummary
            {
                Substrate = "kafkapostgres",
                RunDate = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero),
                Saturation = new SaturationSummaryRow { EventsPerSecond = 373 },
            });

            var read = await SubstrateSummaryStore.ReadAsync(path);

            Assert.NotNull(read);
            Assert.Equal(new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero), read!.RunDate);
            Assert.Equal(373, read.Saturation!.EventsPerSecond);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task EndToEnd_SingleSubstrateRefresh_PreservesOtherSubstrateRowsInRenderedMarkdown()
    {
        // Simulates the user's workflow: an azure summary already lives on
        // disk from a prior run; this run only refreshes kafkapostgres. The
        // rendered markdown must contain rows for both substrates and the
        // run_date token for both must be resolved.
        var directory = Path.Combine(Path.GetTempPath(), $"edict-summary-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await SubstrateSummaryStore.WriteAsync(
                SubstrateSummaryStore.PathFor(directory, "azure"),
                new SubstrateSummary
                {
                    Substrate = "azure",
                    RunDate = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero),
                    ClosedLoop =
                    [
                        new ClosedLoopSummaryRow
                        {
                            Scenario = "Command acceptance",
                            Parallelism = 2,
                            P50Ms = 11.95,
                            P95Ms = 18.56,
                            P99Ms = 25.52,
                            Health = new RunHealthSummary { Succeeded = 100 },
                        },
                    ],
                    Saturation = new SaturationSummaryRow
                    {
                        EventsPerSecond = 70,
                        WindowSeconds = 30,
                        ProducerConcurrency = 256,
                        AggregateCount = 1024,
                        Health = new RunHealthSummary { Succeeded = 2100 },
                    },
                });
            await SubstrateSummaryStore.WriteAsync(
                SubstrateSummaryStore.PathFor(directory, "kafkapostgres"),
                SubstrateSummaryStore.BuildFromResults(
                    substrate: "kafkapostgres",
                    runDate: new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero),
                    closedLoop:
                    [
                        new ThroughputResults(
                            Substrate: "kafkapostgres",
                            Scenario: "Command acceptance",
                            Parallelism: 2,
                            CompletedCount: 25_609,
                            ElapsedMeasurement: TimeSpan.FromSeconds(30),
                            Latency: new LatencyResults(
                                P50: TimeSpan.FromMilliseconds(1.84),
                                P95: TimeSpan.FromMilliseconds(4.66),
                                P99: TimeSpan.FromMilliseconds(7.46)),
                            Health: new RunHealth(
                                Succeeded: 25_609,
                                Failed: 0,
                                FailuresByType: ImmutableSortedDictionary<string, long>.Empty)),
                    ],
                    saturation: new SaturationResults(
                        Substrate: "kafkapostgres",
                        EventsPerSecond: 373,
                        WindowSeconds: 30,
                        ProducerConcurrency: 256,
                        AggregateCount: 1024,
                        Health: new RunHealth(
                            Succeeded: 11_188,
                            Failed: 0,
                            FailuresByType: ImmutableSortedDictionary<string, long>.Empty))));

            var summaries = await SubstrateSummaryStore.ReadAllAsync(directory);
            var hydrated = SubstrateSummaryStore.Hydrate(summaries);

            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (substrateName, runDate) in hydrated.RunDates)
            {
                tokens["run_date:" + substrateName] = runDate.ToString("yyyy-MM-dd");
            }

            var template =
                "azure={{run_date:azure}} kafka={{run_date:kafkapostgres}}\n" +
                "{{table:saturation}}\n" +
                "{{table:closed_loop}}\n";
            var output = MarkdownWriter.Render(template, tokens, hydrated.ClosedLoop, hydrated.Saturation);

            await Verify(output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BuildFromResults_FiltersOutOtherSubstratesFromClosedLoopList()
    {
        // The runner accumulates a single combined list across substrates; the
        // builder must scope its summary to the named substrate so a sidecar
        // file never picks up rows that belong to a different substrate.
        var summary = SubstrateSummaryStore.BuildFromResults(
            substrate: "azure",
            runDate: DateTimeOffset.UtcNow,
            closedLoop:
            [
                new ThroughputResults(
                    Substrate: "azure",
                    Scenario: "Command acceptance",
                    Parallelism: 2,
                    CompletedCount: 100,
                    ElapsedMeasurement: TimeSpan.FromSeconds(30),
                    Latency: new LatencyResults(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                    Health: RunHealth.Empty),
                new ThroughputResults(
                    Substrate: "kafkapostgres",
                    Scenario: "Command acceptance",
                    Parallelism: 2,
                    CompletedCount: 200,
                    ElapsedMeasurement: TimeSpan.FromSeconds(30),
                    Latency: new LatencyResults(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                    Health: RunHealth.Empty),
            ],
            saturation: null);

        var row = Assert.Single(summary.ClosedLoop);
        Assert.Equal(100, row.CompletedCount);
    }
}
