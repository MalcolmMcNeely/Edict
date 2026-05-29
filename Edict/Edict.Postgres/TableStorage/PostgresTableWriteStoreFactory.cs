using Edict.Contracts.TableStorage;
using Edict.Core.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using NpgsqlTypes;

using Orleans.Serialization;

namespace Edict.Postgres.TableStorage;

/// <summary>
/// Postgres implementation of <see cref="IEdictTableStoreFactory"/>. Creates
/// a generic projection table on demand (idempotent <c>CREATE TABLE IF NOT
/// EXISTS</c>) and hands back a <see cref="PostgresTableWriteStore{T}"/>
/// bound to it. The factory also services the framework-internal one-shot
/// <see cref="UpsertRowAsync"/> overload used by the Outbox's UpsertRow
/// effect executor.
/// </summary>
public sealed class PostgresTableWriteStoreFactory : IEdictTableStoreFactory
{
    readonly NpgsqlDataSource _dataSource;
    readonly Serializer _serializer;
    readonly IServiceProvider? _services;

    public PostgresTableWriteStoreFactory(NpgsqlDataSource dataSource, Serializer serializer)
        : this(dataSource, serializer, services: null) { }

    public PostgresTableWriteStoreFactory(NpgsqlDataSource dataSource, Serializer serializer, IServiceProvider? services)
    {
        _dataSource = dataSource;
        _serializer = serializer;
        _services = services;
    }

    public async Task<IEdictTableWriteStore<T>> CreateAsync<T>(string tableName, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        await PostgresTableSchema.EnsureProjectionTableAsync(_dataSource, tableName, cancellationToken);
        return new PostgresTableWriteStore<T>(_dataSource, tableName, _serializer);
    }

    public async Task UpsertRowAsync(
        string tableName,
        string partitionKey,
        string rowKey,
        object row,
        CancellationToken cancellationToken = default)
    {
        await PostgresTableSchema.EnsureProjectionTableAsync(_dataSource, tableName, cancellationToken);

        var quoted = PostgresTableSchema.QuoteIdentifier(tableName);
        // The row arrives as an object (the Outbox UpsertRow executor
        // deserialises from JSON to the concrete type), so dispatch
        // SerializeToArray<T> at the runtime type. Reflection here is fine —
        // each effect drain is a per-grain-turn ceremony, not a hot loop.
        var bytes = SerializeRow(row);
        var etag = Guid.NewGuid().ToString("N");

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"INSERT INTO {quoted} (partition_key, row_key, payload, etag) " +
                "VALUES (@pk, @rk, @payload, @etag) " +
                "ON CONFLICT (partition_key, row_key) DO UPDATE SET " +
                "payload = EXCLUDED.payload, etag = EXCLUDED.etag;";
            command.Parameters.AddWithValue("pk", partitionKey);
            command.Parameters.AddWithValue("rk", rowKey);
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea) { Value = bytes });
            command.Parameters.AddWithValue("etag", etag);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception,
                $"UpsertRowAsync failed for {tableName} ({partitionKey}/{rowKey})");
        }
    }

    byte[] SerializeRow(object row)
    {
        var rowType = row.GetType();
        // Prefer the DI-resolved typed serializer when a service provider is
        // available — Orleans materialises a <see cref="Serializer{T}"/> per
        // type via DI, so the runtime cost is a single dictionary lookup. The
        // reflection fallback is here for callers that constructed the
        // factory without a service provider (extension-call-time
        // registration).
        if (_services is not null)
        {
            var serializerType = typeof(Serializer<>).MakeGenericType(rowType);
            var typedSerializer = _services.GetRequiredService(serializerType);
            // Orleans 10.x signature: `byte[] SerializeToArray(in T value, int sizeHint = 0)`.
            // The `in` modifier makes the parameter a by-ref type at the metadata level,
            // so a plain GetMethod(..., [rowType, typeof(int)]) returns null. Iterate
            // candidate overloads and pick the one whose first parameter's element
            // type matches the row type.
            var method = serializerType.GetMethods()
                .FirstOrDefault(m =>
                {
                    if (m.Name != nameof(Serializer<object>.SerializeToArray))
                    {
                        return false;
                    }

                    var parameters = m.GetParameters();
                    if (parameters.Length == 0)
                    {
                        return false;
                    }

                    var first = parameters[0].ParameterType;
                    var elementType = first.IsByRef ? first.GetElementType() : first;
                    return elementType == rowType;
                })
                ?? throw new InvalidOperationException(
                    $"Serializer<{rowType.FullName}>.SerializeToArray with the expected shape not found.");
            var parameters2 = method.GetParameters();
            var args2 = new object?[parameters2.Length];
            args2[0] = row;
            for (var i = 1; i < args2.Length; i++)
            {
                args2[i] = parameters2[i].HasDefaultValue ? parameters2[i].DefaultValue : null;
            }
            var bytes = method.Invoke(typedSerializer, args2)
                ?? throw new InvalidOperationException(
                    $"Serializer<{rowType.FullName}>.SerializeToArray returned null.");
            return (byte[])bytes;
        }

        var openMethod = typeof(Serializer).GetMethods()
            .FirstOrDefault(m => m.Name == nameof(Serializer.SerializeToArray)
                && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1)
            ?? throw new InvalidOperationException(
                "Orleans Serializer.SerializeToArray<T> not found.");
        var parameters = openMethod.GetParameters();
        var args = new object?[parameters.Length];
        args[0] = row;
        for (var i = 1; i < args.Length; i++)
        {
            args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
        }
        var serialized = openMethod.MakeGenericMethod(rowType).Invoke(_serializer, args)
            ?? throw new InvalidOperationException(
                $"SerializeToArray returned null for {rowType.FullName}.");
        return (byte[])serialized;
    }
}
