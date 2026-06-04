using Edict.Postgres;
using Edict.Postgres.Persistence;
using Edict.Tests.Conformance.Projections;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

using Xunit;

namespace Edict.Postgres.Tests.Persistence;

/// <summary>
/// Direct exercises of <see cref="EdictPostgresGrainStorage"/> against the
/// per-fixture Postgres database. The provider exists to keep the
/// optimistic-concurrency contract Orleans depends on (issue 9737), so these
/// pin the version round-trip, both conflict paths, clear, and the
/// NpgsqlException-to-EdictPostgresStorageException translation that lets a
/// connection fault survive the Orleans message-serializer hop.
/// </summary>
[Collection(PostgresProviderCollection.Name)]
public sealed class EdictPostgresGrainStorageTests
{
    readonly NpgsqlDataSource _dataSource;
    readonly Serializer _serializer;
    readonly IServiceProvider _services;

    public EdictPostgresGrainStorageTests(PostgresProviderFixture fixture)
    {
        _dataSource = new NpgsqlDataSourceBuilder(fixture.PostgresConnectionString).Build();
        var services = new ServiceCollection();
        services.AddSerializer(serializer => serializer.AddAssembly(typeof(OrderTableRow).Assembly));
        _services = services.BuildServiceProvider();
        _serializer = _services.GetRequiredService<Serializer>();
    }

    EdictPostgresGrainStorage CreateStorage() =>
        new(_dataSource, "edict-test", _serializer, _services, NullLogger<EdictPostgresGrainStorage>.Instance);

    [Fact]
    public async Task WriteThenRead_RoundTripsStateAndStartsVersionAtOne()
    {
        // Arrange
        var storage = CreateStorage();
        var grainId = NewGrainId();
        var writeState = new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 7 } };

        // Act
        await storage.WriteStateAsync("state", grainId, writeState);

        var readState = new TestGrainState<OrderTableRow>();
        await storage.ReadStateAsync("state", grainId, readState);

        // Assert
        Assert.Equal("1", writeState.ETag);
        Assert.True(writeState.RecordExists);
        Assert.True(readState.RecordExists);
        Assert.Equal("1", readState.ETag);
        Assert.Equal(7, readState.State.OrderCount);
    }

    [Fact]
    public async Task Read_MissingRow_ReturnsFreshStateMarkedNotExisting()
    {
        // Arrange
        var storage = CreateStorage();
        var grainId = NewGrainId();
        var readState = new TestGrainState<OrderTableRow> { ETag = "stale", RecordExists = true };

        // Act
        await storage.ReadStateAsync("state", grainId, readState);

        // Assert
        Assert.False(readState.RecordExists);
        Assert.Null(readState.ETag);
        Assert.Equal(0, readState.State.OrderCount);
    }

    [Fact]
    public async Task Write_WithStaleETag_RaisesInconsistentState()
    {
        // Arrange
        var storage = CreateStorage();
        var grainId = NewGrainId();
        var current = new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 1 } };
        await storage.WriteStateAsync("state", grainId, current);
        await storage.WriteStateAsync("state", grainId, current);

        // A writer still holding version 1 after the row advanced to version 2.
        var stale = new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 99 }, ETag = "1" };

        // Act + Assert
        await Assert.ThrowsAsync<InconsistentStateException>(
            () => storage.WriteStateAsync("state", grainId, stale));
    }

    [Fact]
    public async Task Write_ConcurrentFirstInsert_RaisesInconsistentState()
    {
        // Arrange
        var storage = CreateStorage();
        var grainId = NewGrainId();
        var first = new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 1 } };
        await storage.WriteStateAsync("state", grainId, first);

        // A second activation that also believes no row exists yet (null ETag).
        var second = new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 2 } };

        // Act + Assert
        await Assert.ThrowsAsync<InconsistentStateException>(
            () => storage.WriteStateAsync("state", grainId, second));
    }

    [Fact]
    public async Task Clear_RemovesRow_SoNextReadReportsNotExisting()
    {
        // Arrange
        var storage = CreateStorage();
        var grainId = NewGrainId();
        var state = new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 5 } };
        await storage.WriteStateAsync("state", grainId, state);

        // Act
        await storage.ClearStateAsync("state", grainId, state);

        var readState = new TestGrainState<OrderTableRow>();
        await storage.ReadStateAsync("state", grainId, readState);

        // Assert
        Assert.False(state.RecordExists);
        Assert.Null(state.ETag);
        Assert.False(readState.RecordExists);
    }

    [Fact]
    public async Task ReadStateAsync_ConnectionFails_ThrowsEdictPostgresStorageException()
    {
        var storage = CreateUnreachableStorage();

        await Assert.ThrowsAsync<EdictPostgresStorageException>(
            () => storage.ReadStateAsync("state", NewGrainId(), new TestGrainState<OrderTableRow>()));
    }

    [Fact]
    public async Task WriteStateAsync_ConnectionFails_ThrowsEdictPostgresStorageException()
    {
        var storage = CreateUnreachableStorage();
        var state = new TestGrainState<OrderTableRow> { State = new OrderTableRow { OrderCount = 1 } };

        await Assert.ThrowsAsync<EdictPostgresStorageException>(
            () => storage.WriteStateAsync("state", NewGrainId(), state));
    }

    [Fact]
    public async Task ClearStateAsync_ConnectionFails_ThrowsEdictPostgresStorageException()
    {
        var storage = CreateUnreachableStorage();

        await Assert.ThrowsAsync<EdictPostgresStorageException>(
            () => storage.ClearStateAsync("state", NewGrainId(), new TestGrainState<OrderTableRow>()));
    }

    EdictPostgresGrainStorage CreateUnreachableStorage()
    {
        // Port 1 is privileged-and-usually-closed; the connection attempt fails
        // fast and the catch translates the NpgsqlException at every seam.
        var unreachable = new NpgsqlDataSourceBuilder(
            "Host=127.0.0.1;Port=1;Username=u;Password=p;Database=d;Timeout=2;Command Timeout=2").Build();
        return new EdictPostgresGrainStorage(
            unreachable, "edict-test", _serializer, _services, NullLogger<EdictPostgresGrainStorage>.Instance);
    }

    static GrainId NewGrainId() => GrainId.Create("agg", Guid.NewGuid().ToString("N"));

    sealed class TestGrainState<T> : IGrainState<T>
        where T : new()
    {
        public T State { get; set; } = new();
        public string? ETag { get; set; }
        public bool RecordExists { get; set; }
    }
}
