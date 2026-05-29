using Edict.Contracts.ClaimCheck;
using Edict.Postgres.ClaimCheck;
using Edict.Postgres.TableStorage;
using Edict.Tests.Conformance.Projections;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orleans.Serialization;

using Xunit;

namespace Edict.Postgres.Tests;

/// <summary>
/// Targeted unit tests for the Postgres provider seams. These tests do not
/// stand up an Orleans cluster — they exercise the persistence-side surfaces
/// directly against the per-fixture Postgres database so a regression in
/// DDL idempotency, the claim-check store, or the table repository surfaces
/// without dragging the whole conformance battery along.
/// </summary>
[Collection(PostgresClusterCollection.Name)]
public sealed class PostgresProviderUnitTests
{
    readonly string _connectionString;
    readonly NpgsqlDataSource _dataSource;
    readonly Serializer _serializer;
    readonly IServiceProvider _services;

    public PostgresProviderUnitTests(PostgresClusterFixture fixture)
    {
        _connectionString = fixture.PostgresConnectionString;
        _dataSource = new NpgsqlDataSourceBuilder(_connectionString).Build();
        var services = new ServiceCollection();
        services.AddSerializer(s => s.AddAssembly(typeof(OrderTableRow).Assembly));
        _services = services.BuildServiceProvider();
        _serializer = _services.GetRequiredService<Serializer>();
    }

    [Fact]
    public void ClaimCheckStore_ShouldExposeNoDeleteApi()
    {
        // Compile-time check: the IEdictClaimCheckStore contract — and the
        // Postgres implementation — must not surface a Delete affordance.
        // Append-only is load-bearing for the forensic guarantee: a
        // framework bug or config mistake cannot erase the claim-check blob.
        var contractMethods = typeof(IEdictClaimCheckStore).GetMethods()
            .Select(m => m.Name).ToArray();
        Assert.DoesNotContain("DeleteAsync", contractMethods);
        Assert.DoesNotContain("Delete", contractMethods);

        var implMethods = typeof(PostgresClaimCheckStore).GetMethods()
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(PostgresClaimCheckStore))
            .Select(m => m.Name).ToArray();
        Assert.DoesNotContain("DeleteAsync", implMethods);
        Assert.DoesNotContain("Delete", implMethods);
    }

    [Fact]
    public async Task ClaimCheckStore_ShouldRoundTripBytes()
    {
        var store = new PostgresClaimCheckStore(_dataSource, "edict_claim_check");
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x10, 0x20, 0x30, 0x40 };

        var key = await store.PutAsync(payload, CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(key));

        var roundTripped = await store.GetAsync(key, CancellationToken.None);
        Assert.Equal(payload, roundTripped.ToArray());
    }

    [Fact]
    public async Task TableRepository_ShouldRoundTripRowViaMessagePackPayload()
    {
        var tableName = $"unit_round_trip_{Guid.NewGuid():N}";
        var factory = new PostgresTableWriteStoreFactory(_dataSource, _serializer, _services);
        var store = await factory.CreateAsync<OrderTableRow>(tableName);

        var partitionKey = Guid.NewGuid().ToString();
        var rowKey = Guid.NewGuid().ToString();
        await store.UpsertAsync(partitionKey, rowKey, new OrderTableRow { OrderCount = 42 });

        var repository = new PostgresTableRepository<OrderTableRow>(_dataSource, tableName, _serializer);
        var row = await repository.GetAsync(partitionKey, rowKey);
        Assert.NotNull(row);
        Assert.Equal(42, row!.OrderCount);
    }

    [Fact]
    public async Task TableRepository_ShouldRespectCompositeKey()
    {
        var tableName = $"unit_composite_{Guid.NewGuid():N}";
        var factory = new PostgresTableWriteStoreFactory(_dataSource, _serializer, _services);
        var store = await factory.CreateAsync<OrderTableRow>(tableName);

        var partitionKey = Guid.NewGuid().ToString();
        await store.UpsertAsync(partitionKey, "rk-a", new OrderTableRow { OrderCount = 1 });
        await store.UpsertAsync(partitionKey, "rk-b", new OrderTableRow { OrderCount = 2 });
        await store.UpsertAsync("other-pk", "rk-a", new OrderTableRow { OrderCount = 99 });

        var repository = new PostgresTableRepository<OrderTableRow>(_dataSource, tableName, _serializer);
        var a = await repository.GetAsync(partitionKey, "rk-a");
        var b = await repository.GetAsync(partitionKey, "rk-b");
        Assert.Equal(1, a!.OrderCount);
        Assert.Equal(2, b!.OrderCount);
    }

    [Fact]
    public async Task TableRepository_ShouldRangeScanByPartition()
    {
        var tableName = $"unit_range_{Guid.NewGuid():N}";
        var factory = new PostgresTableWriteStoreFactory(_dataSource, _serializer, _services);
        var store = await factory.CreateAsync<OrderTableRow>(tableName);

        var partitionKey = Guid.NewGuid().ToString();
        for (var i = 1; i <= 5; i++)
        {
            await store.UpsertAsync(partitionKey, $"rk-{i}", new OrderTableRow { OrderCount = i });
        }
        await store.UpsertAsync("other-pk", "rk-1", new OrderTableRow { OrderCount = 999 });

        var repository = new PostgresTableRepository<OrderTableRow>(_dataSource, tableName, _serializer);
        var rows = await repository.QueryPartitionAsync(partitionKey);
        Assert.Equal(5, rows.Count);
        Assert.Equal([1, 2, 3, 4, 5], rows.Select(r => r.OrderCount).OrderBy(c => c).ToArray());
    }

    [Fact]
    public async Task TableRepository_ShouldReturnNullForMissingRow()
    {
        var tableName = $"unit_missing_{Guid.NewGuid():N}";
        var factory = new PostgresTableWriteStoreFactory(_dataSource, _serializer, _services);
        await factory.CreateAsync<OrderTableRow>(tableName);

        var repository = new PostgresTableRepository<OrderTableRow>(_dataSource, tableName, _serializer);
        var row = await repository.GetAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        Assert.Null(row);
    }

    [Fact]
    public async Task TableRepository_ShouldReturnEmpty_WhenTableDoesNotExist()
    {
        var repository = new PostgresTableRepository<OrderTableRow>(
            _dataSource, $"unit_nonexistent_{Guid.NewGuid():N}", _serializer);

        var rows = await repository.QueryPartitionAsync("anything");
        Assert.Empty(rows);
    }

    [Fact]
    public async Task DdlBootstrap_ShouldBeIdempotentOnRerun()
    {
        // The fixture already ran the bootstrap once when the silo wired
        // AddEdictPostgresPersistence. Re-run synchronously and confirm the
        // existing tables stay in place — no DROP, no rename, no exception.
        var tablesBefore = await ListPublicTablesAsync();
        Edict.Postgres.Bootstrap.PostgresDdlBootstrap.Run(_dataSource);
        var tablesAfter = await ListPublicTablesAsync();

        Assert.Equal(tablesBefore.OrderBy(t => t), tablesAfter.OrderBy(t => t));
        Assert.Contains("orleansstorage", tablesAfter);
        Assert.Contains("orleansreminderstable", tablesAfter);
        Assert.Contains("edict_grain_state", tablesAfter);
        Assert.Contains("edict_claim_check", tablesAfter);
        Assert.Contains("deadletter", tablesAfter);
    }

    async Task<IReadOnlyList<string>> ListPublicTablesAsync()
    {
        var tables = new List<string>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT tablename FROM pg_tables WHERE schemaname = 'public';";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }
}
