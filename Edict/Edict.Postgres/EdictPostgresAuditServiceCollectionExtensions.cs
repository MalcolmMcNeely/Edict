using Edict.Contracts.Audit;
using Edict.Core.Audit;
using Edict.Core.Configuration;
using Edict.Postgres.Audit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Orleans.Serialization;

namespace Edict.Postgres;

/// <summary>
/// Client-side Postgres audit-read extension on <see cref="IServiceCollection"/>.
/// The silo captures the audit log through
/// <see cref="EdictPostgresSiloBuilderExtensions.AddEdictPostgresPersistence"/>
/// plus <c>WithAudit()</c>; a separate process that only reads it (an Orleans
/// client, a web host) registers the Postgres audit stores against the same
/// database and resolves an <see cref="IEdictAuditRepository"/> over them, with
/// no grain storage, reminders, or capture path.
/// </summary>
public static class EdictPostgresAuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Postgres-backed audit stores and the read-only
    /// <see cref="IEdictAuditRepository"/> over them. Configure the same
    /// <see cref="EdictPostgresPersistenceOptions.ConnectionString"/> and audit
    /// table names the capturing silo uses so the reader points at the same
    /// records. Requires an Orleans <c>Serializer</c> in the container (an
    /// Orleans client host already registers one).
    /// </summary>
    public static IServiceCollection AddEdictPostgresAuditReader(
        this IServiceCollection services,
        Action<EdictPostgresPersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new EdictPostgresPersistenceOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new EdictWiringException(
                "AddEdictPostgresAuditReader requires EdictPostgresPersistenceOptions.ConnectionString.");
        }

        // One read-only pool owned here, closed over by the store factories so it
        // is used regardless of any other data source the host registers.
        var dataSource = EdictPostgresSiloBuilderExtensions.BuildDataSource(options);
        var auditTable = options.AuditTableName;
        var auditPayloadTable = options.AuditPayloadTableName;

        services.TryAddSingleton<IEdictAuditStore>(serviceProvider =>
            new PostgresAuditStore(dataSource, serviceProvider.GetRequiredService<Serializer>(), auditTable));
        services.TryAddSingleton<IEdictAuditPayloadStore>(_ =>
            new PostgresAuditPayloadStore(dataSource, auditPayloadTable));

        return services.AddEdictAuditReader();
    }
}
