using Edict.Postgres;
using Edict.Postgres.ClaimCheck;
using Edict.Postgres.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orleans.Serialization;

using Xunit;

using static VerifyXunit.Verifier;

namespace Edict.Postgres.Tests;

/// <summary>
/// Round-trip proofs for <see cref="EdictPostgresStorageException"/>. The whole
/// point of the type is that it survives the Orleans message-serializer hop
/// where <see cref="NpgsqlException"/> does not — these tests pin the load-bearing
/// property without standing up a cluster or talking to a database.
/// </summary>
public sealed class EdictPostgresStorageExceptionTests
{
    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(s => s.AddAssembly(typeof(EdictPostgresStorageException).Assembly));
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    [Fact]
    public Task ShouldRoundTripMessageAndNativeFields_ThroughOrleansSerializer()
    {
        var serializer = BuildSerializer();
        var original = new EdictPostgresStorageException(
            message: "WriteStateAsync failed for grain agg/abc state 'state': boom",
            nativeType: "Npgsql.NpgsqlException",
            nativeMessage: "boom");

        var bytes = serializer.SerializeToArray(original);
        var roundTripped = serializer.Deserialize<EdictPostgresStorageException>(bytes);

        return Verify(new { roundTripped.Message, roundTripped.NativeType, roundTripped.NativeMessage });
    }

    [Fact]
    public Task FromNpgsqlException_ShouldCaptureRuntimeTypeAndMessageVerbatim()
    {
        // NpgsqlException is constructable directly with a message string —
        // good enough to assert the static factory's shape without forcing a
        // real connection failure.
        var native = new NpgsqlException("connection pool exhausted");

        var translated = EdictPostgresStorageException.From(native, "WriteStateAsync failed for grain X");

        return Verify(new { translated.Message, translated.NativeType, translated.NativeMessage });
    }

    [Fact]
    public void ShouldNotCarryInnerException_SoOrleansNeverWalksBackToNpgsql()
    {
        // The load-bearing invariant: the original NpgsqlException must NOT
        // be attached as InnerException, otherwise the Orleans serializer
        // walks the chain and hits the same CodecNotFoundException this whole
        // type exists to avoid.
        var native = new NpgsqlException("boom");

        var translated = EdictPostgresStorageException.From(native, "ctx");

        Assert.Null(translated.InnerException);
    }

    // Per-seam translation tests: each Postgres-touching surface translates a
    // connection-level NpgsqlException at every method that could be called
    // inside a grain turn. Port 1 is privileged-and-usually-closed; the bogus
    // connection string short-circuits before any real Postgres call.
    const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Username=u;Password=p;Database=d;Timeout=2;Command Timeout=2";

    static NpgsqlDataSource UnreachableDataSource() =>
        new NpgsqlDataSourceBuilder(UnreachableConnectionString).Build();

    static Serializer EmptySerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer();
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    sealed class DummyRow
    {
        public string? Value { get; set; }
    }

    [Fact]
    public async Task TableRepository_GetAsync_ShouldThrowEdictPostgresStorageException_WhenConnectionFails()
    {
        var repo = new PostgresTableRepository<DummyRow>(UnreachableDataSource(), "dummy_table", EmptySerializer());

        await Assert.ThrowsAsync<EdictPostgresStorageException>(
            () => repo.GetAsync("pk", "rk"));
    }

    [Fact]
    public async Task TableRepository_QueryPartitionAsync_ShouldThrowEdictPostgresStorageException_WhenConnectionFails()
    {
        var repo = new PostgresTableRepository<DummyRow>(UnreachableDataSource(), "dummy_table", EmptySerializer());

        await Assert.ThrowsAsync<EdictPostgresStorageException>(
            () => repo.QueryPartitionAsync("pk"));
    }

    [Fact]
    public async Task ClaimCheckStore_PutAsync_ShouldThrowEdictPostgresStorageException_WhenConnectionFails()
    {
        var store = new PostgresClaimCheckStore(UnreachableDataSource(), "edict_claim_check");

        await Assert.ThrowsAsync<EdictPostgresStorageException>(
            () => store.PutAsync(new byte[] { 0x01 }, CancellationToken.None));
    }

    [Fact]
    public async Task ClaimCheckStore_GetAsync_ShouldThrowEdictPostgresStorageException_WhenConnectionFails()
    {
        var store = new PostgresClaimCheckStore(UnreachableDataSource(), "edict_claim_check");

        await Assert.ThrowsAsync<EdictPostgresStorageException>(
            () => store.GetAsync(Guid.NewGuid().ToString("N"), CancellationToken.None));
    }

    [Fact]
    public async Task TableWriteStoreFactory_UpsertRowAsync_ShouldThrowEdictPostgresStorageException_WhenConnectionFails()
    {
        var factory = new PostgresTableWriteStoreFactory(UnreachableDataSource(), EmptySerializer());

        await Assert.ThrowsAsync<EdictPostgresStorageException>(
            () => factory.UpsertRowAsync("dummy_table", "pk", "rk", new DummyRow { Value = "x" }));
    }
}
