using System.Diagnostics.Metrics;

using Edict.Postgres;
using Edict.Postgres.Persistence;
using Edict.Telemetry;
using Edict.Tests.Conformance.Projections;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

using Xunit;

namespace Edict.Postgres.Tests.Persistence;

/// <summary>
/// Seam-level proof that all three <see cref="EdictPostgresGrainStorage"/> storage
/// methods wrap their full body — connection acquisition included — in the transient
/// retry. Drives a throwing connection-opener (the only fakeable seam, since
/// <see cref="NpgsqlDataSource.OpenConnectionAsync()"/> is non-virtual) so a transient
/// fault recovers on a later attempt or exhausts the budget. Asserts the
/// externally-observable effects: the retry counter, the exhaustion warning, and the
/// surfaced exception type.
/// </summary>
[Collection(PostgresProviderCollection.Name)]
public sealed class EdictPostgresGrainStorageRetryTests
{
    static readonly PostgresTransientRetry.Policy FastPolicy = new(TotalAttempts: 3, BaseDelay: TimeSpan.FromMilliseconds(5));

    readonly NpgsqlDataSource _dataSource;
    readonly Serializer _serializer;
    readonly IServiceProvider _services;

    public EdictPostgresGrainStorageRetryTests(PostgresProviderFixture fixture)
    {
        _dataSource = new NpgsqlDataSourceBuilder(fixture.PostgresConnectionString).Build();
        var services = new ServiceCollection();
        services.AddSerializer(serializer => serializer.AddAssembly(typeof(OrderTableRow).Assembly));
        _services = services.BuildServiceProvider();
        _serializer = _services.GetRequiredService<Serializer>();
    }

    EdictPostgresGrainStorage CreateStorage(ILogger<EdictPostgresGrainStorage>? logger = null) =>
        new(_dataSource, "edict-test", _serializer, _services, logger ?? NullLogger<EdictPostgresGrainStorage>.Instance, FastPolicy);

    [Fact]
    public async Task ReadStateAsync_RecoversAfterTransientOpen_RecordsRecoveredAndReturns()
    {
        // Arrange
        var storage = CreateStorage();
        FailFirstOpen(storage);
        var captures = new List<RetryCapture>();
        using var listener = CaptureRetryCounter(captures);

        // Act
        var readState = new TestGrainState<OrderTableRow>();
        await storage.ReadStateAsync("state", NewGrainId(), readState);

        // Assert
        Assert.False(readState.RecordExists);
        Assert.Contains(captures, capture => capture is { Operation: PostgresStorageRetryTelemetry.Read, Outcome: PostgresStorageRetryTelemetry.Recovered });
    }

    [Fact]
    public async Task ReadStateAsync_ExhaustsTransientOpens_RecordsExhaustedWarnsAndThrows()
    {
        // Arrange
        var logger = new CapturingLogger();
        var storage = CreateStorage(logger);
        storage.ConnectionOpener = () => throw new NpgsqlException("always transient", new IOException());
        var captures = new List<RetryCapture>();
        using var listener = CaptureRetryCounter(captures);

        // Act + Assert
        await Assert.ThrowsAsync<EdictPostgresStorageException>(
            () => storage.ReadStateAsync("state", NewGrainId(), new TestGrainState<OrderTableRow>()));

        Assert.Contains(captures, capture => capture is { Operation: PostgresStorageRetryTelemetry.Read, Outcome: PostgresStorageRetryTelemetry.Exhausted });
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task WriteStateAsync_RecoversAfterTransientOpen_RecordsRecoveredAndWritesRow()
    {
        // Arrange
        var storage = CreateStorage();
        FailFirstOpen(storage);
        var captures = new List<RetryCapture>();
        using var listener = CaptureRetryCounter(captures);

        // Act
        var writeState = new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 4 } };
        await storage.WriteStateAsync("state", NewGrainId(), writeState);

        // Assert
        Assert.Equal("1", writeState.ETag);
        Assert.Contains(captures, capture => capture is { Operation: PostgresStorageRetryTelemetry.Write, Outcome: PostgresStorageRetryTelemetry.Recovered });
    }

    [Fact]
    public async Task ClearStateAsync_RecoversAfterTransientOpen_RecordsRecovered()
    {
        // Arrange
        var storage = CreateStorage();
        var grainId = NewGrainId();
        await storage.WriteStateAsync("state", grainId, new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 2 } });
        FailFirstOpen(storage);
        var captures = new List<RetryCapture>();
        using var listener = CaptureRetryCounter(captures);

        // Act
        var state = new TestGrainState<OrderTableRow> { ETag = "1", RecordExists = true };
        await storage.ClearStateAsync("state", grainId, state);

        // Assert
        Assert.False(state.RecordExists);
        Assert.Contains(captures, capture => capture is { Operation: PostgresStorageRetryTelemetry.Clear, Outcome: PostgresStorageRetryTelemetry.Recovered });
    }

    [Fact]
    public async Task WriteStateAsync_NonTransientOpen_ThrowsImmediatelyWithoutRetry()
    {
        // Arrange
        var storage = CreateStorage();
        var opens = 0;
        storage.ConnectionOpener = () =>
        {
            Interlocked.Increment(ref opens);
            throw new NpgsqlException("non-transient");
        };

        // Act + Assert
        await Assert.ThrowsAsync<EdictPostgresStorageException>(
            () => storage.WriteStateAsync("state", NewGrainId(), new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 1 } }));

        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task WriteStateAsync_StaleETag_PropagatesInconsistentStateUnretried()
    {
        // Arrange
        var storage = CreateStorage();
        var grainId = NewGrainId();
        var current = new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 1 } };
        await storage.WriteStateAsync("state", grainId, current);
        await storage.WriteStateAsync("state", grainId, current);

        var opens = 0;
        var realOpener = storage.ConnectionOpener;
        storage.ConnectionOpener = () =>
        {
            Interlocked.Increment(ref opens);
            return realOpener();
        };
        var stale = new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 99 }, ETag = "1" };

        // Act + Assert
        await Assert.ThrowsAsync<InconsistentStateException>(
            () => storage.WriteStateAsync("state", grainId, stale));

        Assert.Equal(1, opens);
    }

    static void FailFirstOpen(EdictPostgresGrainStorage storage)
    {
        var realOpener = storage.ConnectionOpener;
        var calls = 0;
        storage.ConnectionOpener = () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new NpgsqlException("transient socket reset", new IOException());
            }
            return realOpener();
        };
    }

    static MeterListener CaptureRetryCounter(List<RetryCapture> captures)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == PostgresStorageRetryTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var dictionary = new Dictionary<string, object?>(tags.Length);
            foreach (var tag in tags)
            {
                dictionary[tag.Key] = tag.Value;
            }
            var operation = dictionary.GetValueOrDefault(PostgresStorageRetryTelemetry.OperationTag) as string;
            var outcome = dictionary.GetValueOrDefault(PostgresStorageRetryTelemetry.OutcomeTag) as string;
            lock (captures)
            {
                captures.Add(new RetryCapture(operation, outcome, value));
            }
        });
        listener.Start();
        return listener;
    }

    static GrainId NewGrainId() => GrainId.Create("agg", Guid.NewGuid().ToString("N"));

    sealed record RetryCapture(string? Operation, string? Outcome, long Value);

    sealed class CapturingLogger : ILogger<EdictPostgresGrainStorage>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    sealed class TestGrainState<T> : IGrainState<T>
        where T : new()
    {
        public T State { get; set; } = new();
        public string? ETag { get; set; }
        public bool RecordExists { get; set; }
    }
}
