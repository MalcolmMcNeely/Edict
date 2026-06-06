using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace Edict.Postgres.Persistence;

/// <summary>
/// Postgres-native <see cref="IGrainStorage"/> for the <c>edict-state</c>
/// provider. Replaces Orleans 10's shipped <c>AdoNetGrainStorage</c> because
/// the shipped provider has a regression that hard-codes the literal
/// <c>"state"</c> as the row-key discriminator instead of the grain type:
/// every <c>Grain&lt;T&gt;</c> sharing a grain id collapses into the same row
/// and the writers race on ETag, which on the Edict normative pattern
/// (a command handler + one or more per-aggregate projection grains, all
/// keyed by the same <c>[EdictRouteKey]</c> Guid) silently strands every
/// projection write. See <see href="https://github.com/dotnet/orleans/issues/9737"/>.
/// This replacement keys on <c>(grain_type, grain_id, state_name, service_id)</c>
/// so concept-level grains stay distinct.
/// <para>
/// Schema (created by <c>PostgresDdlBootstrap</c>):
/// <c>edict_grain_state(grain_type TEXT, grain_id TEXT, state_name TEXT,
/// service_id TEXT, payload BYTEA, version INT, modified_on TIMESTAMPTZ)</c>
/// with PRIMARY KEY <c>(grain_type, grain_id, state_name, service_id)</c>.
/// Version is the ETag — a parameterised UPDATE-and-return-new-version pattern
/// keeps the optimistic-concurrency contract Orleans expects.
/// </para>
/// <para>
/// Every seam wraps its full body — connection acquisition included — in a
/// transient-fault retry. The Azure pairing gets this for free from the Azure
/// Storage SDK; because this provider rolls its own grain storage it gets none,
/// so a transient socket reset that the Azure pairing silently absorbs would
/// otherwise fail a Postgres grain activation outright. Retry is gated on
/// <see cref="NpgsqlException.IsTransient"/>; a non-transient <see cref="NpgsqlException"/>
/// surfaces immediately as <see cref="EdictPostgresStorageException"/> with no retry,
/// and <see cref="InconsistentStateException"/> is not an <see cref="NpgsqlException"/>
/// so it propagates unretried as the concurrency signal. Retrying a write is safe even
/// when the first attempt may already have applied: the version check returns no row on
/// a no-op, so a double-apply degrades to an <see cref="InconsistentStateException"/>
/// that Orleans handles by deactivating and re-reading. Count and base delay are tuned
/// via <see cref="EdictPostgresPersistenceOptions.StorageRetryCount"/> and
/// <see cref="EdictPostgresPersistenceOptions.StorageRetryBaseDelay"/>.
/// </para>
/// </summary>
internal sealed class EdictPostgresGrainStorage : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
{
    readonly NpgsqlDataSource _dataSource;
    readonly string _serviceId;
    readonly Serializer _serializer;
    readonly IServiceProvider _services;
    readonly ILogger<EdictPostgresGrainStorage> _logger;
    readonly PostgresTransientRetry.Policy _retryPolicy;
    Func<ValueTask<NpgsqlConnection>> _openConnection;

    public EdictPostgresGrainStorage(
        NpgsqlDataSource dataSource,
        string serviceId,
        Serializer serializer,
        IServiceProvider services,
        ILogger<EdictPostgresGrainStorage> logger,
        PostgresTransientRetry.Policy retryPolicy)
    {
        _dataSource = dataSource;
        _serviceId = serviceId;
        _serializer = serializer;
        _services = services;
        _logger = logger;
        _retryPolicy = retryPolicy;
        _openConnection = () => _dataSource.OpenConnectionAsync();
    }

    // The connection-acquisition seam, retried inside every storage method.
    // Defaults to the shared data source; a test substitutes a throwing opener
    // to drive the transient-fault path without a live database fault.
    internal Func<ValueTask<NpgsqlConnection>> ConnectionOpener
    {
        get => _openConnection;
        set => _openConnection = value;
    }

    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var grainType = grainId.Type.ToString() ?? "";
        var grainIdText = grainId.Key.ToString() ?? "";

        try
        {
            await PostgresTransientRetry.ExecuteAsync(
                async () =>
                {
                    await using var connection = await _openConnection();
                    await using var command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT payload, version FROM edict_grain_state " +
                        "WHERE grain_type = @grain_type AND grain_id = @grain_id " +
                        "AND state_name = @state_name AND service_id = @service_id;";
                    command.Parameters.AddWithValue("grain_type", grainType);
                    command.Parameters.AddWithValue("grain_id", grainIdText);
                    command.Parameters.AddWithValue("state_name", stateName);
                    command.Parameters.AddWithValue("service_id", _serviceId);

                    await using var reader = await command.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                    {
                        grainState.State = Activator.CreateInstance<T>()!;
                        grainState.ETag = null;
                        grainState.RecordExists = false;
                        return;
                    }

                    var payload = reader.IsDBNull(0) ? null : (byte[])reader["payload"];
                    var version = reader.GetInt32(1);

                    if (payload is null || payload.Length == 0)
                    {
                        grainState.State = Activator.CreateInstance<T>()!;
                    }
                    else
                    {
                        grainState.State = _serializer.Deserialize<T>(payload);
                    }
                    grainState.ETag = version.ToString();
                    grainState.RecordExists = true;
                },
                _retryPolicy,
                IsTransientFault,
                BuildRetryHook(PostgresStorageRetryTelemetry.Read));
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception,
                $"ReadStateAsync failed for grain {grainType}/{grainIdText} state '{stateName}'");
        }
    }

    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var grainType = grainId.Type.ToString() ?? "";
        var grainIdText = grainId.Key.ToString() ?? "";
        var payload = _serializer.SerializeToArray(grainState.State);

        try
        {
            await PostgresTransientRetry.ExecuteAsync(
                async () =>
                {
                    await using var connection = await _openConnection();

                    if (grainState.ETag is null)
                    {
                        // First write — INSERT (only if no row already exists for this
                        // grain). A row inserted concurrently by another activation makes
                        // this INSERT a no-op via the WHERE NOT EXISTS clause; in that
                        // case the version stays unchanged and we raise an
                        // InconsistentStateException so Orleans deactivates and re-reads.
                        await using var insert = connection.CreateCommand();
                        insert.CommandText =
                            "INSERT INTO edict_grain_state " +
                            "(grain_type, grain_id, state_name, service_id, payload, version, modified_on) " +
                            "VALUES (@grain_type, @grain_id, @state_name, @service_id, @payload, 1, now()) " +
                            "ON CONFLICT (grain_type, grain_id, state_name, service_id) DO NOTHING " +
                            "RETURNING version;";
                        AddParameters(insert, grainType, grainIdText, stateName, _serviceId, payload);
                        var inserted = await insert.ExecuteScalarAsync();
                        if (inserted is null || inserted is DBNull)
                        {
                            throw new InconsistentStateException(
                                $"Concurrent insert detected for grain {grainType}/{grainIdText} state '{stateName}'.");
                        }
                        grainState.ETag = ((int)inserted).ToString();
                        grainState.RecordExists = true;
                        return;
                    }

                    var expectedVersion = int.Parse(grainState.ETag);
                    await using var update = connection.CreateCommand();
                    update.CommandText =
                        "UPDATE edict_grain_state SET payload = @payload, version = version + 1, modified_on = now() " +
                        "WHERE grain_type = @grain_type AND grain_id = @grain_id " +
                        "AND state_name = @state_name AND service_id = @service_id " +
                        "AND version = @expected_version " +
                        "RETURNING version;";
                    AddParameters(update, grainType, grainIdText, stateName, _serviceId, payload);
                    update.Parameters.AddWithValue("expected_version", expectedVersion);
                    var result = await update.ExecuteScalarAsync();
                    if (result is null || result is DBNull)
                    {
                        throw new InconsistentStateException(
                            $"Version conflict writing grain {grainType}/{grainIdText} state '{stateName}': " +
                            $"expected version {expectedVersion}.");
                    }
                    grainState.ETag = ((int)result).ToString();
                    grainState.RecordExists = true;
                },
                _retryPolicy,
                IsTransientFault,
                BuildRetryHook(PostgresStorageRetryTelemetry.Write));
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception,
                $"WriteStateAsync failed for grain {grainType}/{grainIdText} state '{stateName}'");
        }
    }

    public async Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var grainType = grainId.Type.ToString() ?? "";
        var grainIdText = grainId.Key.ToString() ?? "";

        try
        {
            await PostgresTransientRetry.ExecuteAsync(
                async () =>
                {
                    await using var connection = await _openConnection();
                    await using var command = connection.CreateCommand();
                    command.CommandText =
                        "DELETE FROM edict_grain_state " +
                        "WHERE grain_type = @grain_type AND grain_id = @grain_id " +
                        "AND state_name = @state_name AND service_id = @service_id;";
                    command.Parameters.AddWithValue("grain_type", grainType);
                    command.Parameters.AddWithValue("grain_id", grainIdText);
                    command.Parameters.AddWithValue("state_name", stateName);
                    command.Parameters.AddWithValue("service_id", _serviceId);
                    await command.ExecuteNonQueryAsync();

                    grainState.State = Activator.CreateInstance<T>()!;
                    grainState.ETag = null;
                    grainState.RecordExists = false;
                },
                _retryPolicy,
                IsTransientFault,
                BuildRetryHook(PostgresStorageRetryTelemetry.Clear));
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception,
                $"ClearStateAsync failed for grain {grainType}/{grainIdText} state '{stateName}'");
        }
    }

    // A transient Npgsql fault is one the driver itself flags as retryable — a
    // socket reset, a timeout, a connection-pool blip. InconsistentStateException
    // is not an NpgsqlException, so it is never transient and never retried: it is
    // the optimistic-concurrency signal Orleans deactivates and re-reads on.
    internal static bool IsTransientFault(Exception exception) =>
        exception is NpgsqlException { IsTransient: true };

    PostgresTransientRetry.Hook BuildRetryHook(string operation) =>
        (phase, attemptNumber, exception) =>
        {
            switch (phase)
            {
                case PostgresTransientRetry.Phase.Retrying:
                    _logger.LogDebug(exception,
                        "Transient Postgres fault on {Operation} (attempt {Attempt}); retrying.", operation, attemptNumber);
                    break;
                case PostgresTransientRetry.Phase.Recovered:
                    PostgresStorageRetryTelemetry.RecordRecovered(operation);
                    break;
                case PostgresTransientRetry.Phase.Exhausted:
                    _logger.LogWarning(exception,
                        "Transient Postgres fault on {Operation} not cleared after {Attempt} attempts; surfacing EdictPostgresStorageException.",
                        operation, attemptNumber);
                    PostgresStorageRetryTelemetry.RecordExhausted(operation);
                    break;
            }
        };

    public void Participate(ISiloLifecycle observer)
    {
        // No lifecycle hooks needed — the schema is bootstrapped by
        // PostgresDdlBootstrap during AddEdictPostgresPersistence wiring, so
        // the table is guaranteed present by the time the first
        // ReadStateAsync fires.
    }

    static void AddParameters(NpgsqlCommand command, string grainType, string grainId, string stateName, string serviceId, byte[] payload)
    {
        command.Parameters.AddWithValue("grain_type", grainType);
        command.Parameters.AddWithValue("grain_id", grainId);
        command.Parameters.AddWithValue("state_name", stateName);
        command.Parameters.AddWithValue("service_id", serviceId);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea) { Value = payload });
    }
}
