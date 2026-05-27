using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core.TableStorage;
using Edict.Postgres.Bootstrap;
using Edict.Postgres.ClaimCheck;
using Edict.Postgres.Persistence;
using Edict.Postgres.TableStorage;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace Edict.Postgres;

/// <summary>
/// Postgres persistence provider extension on <see cref="ISiloBuilder"/>.
/// Mirrors <c>AddEdictAzurePersistence</c>'s shape — one call per decision
/// the consumer is making — and chains the Orleans AdoNet primitives
/// (<c>AddAdoNetGrainStorage</c>, <c>UseAdoNetReminderService</c>) plus the
/// Edict provider seams (claim-check store, table-store factory, dead-letter
/// table repository) so a Postgres silo's <c>Program.cs</c> reads
/// top-to-bottom as one Action lambda instead of seven interleaved
/// registrations.
/// </summary>
public static class EdictPostgresSiloBuilderExtensions
{
    /// <summary>
    /// Registers Orleans AdoNet grain storage for <c>edict-state</c>, the
    /// Postgres reminder service, the table write-store factory, the
    /// dead-letter table repository, the claim-check Postgres store, and the
    /// <see cref="EdictPersistenceProviderMarker"/> the startup validator
    /// inspects. Idempotently runs the embedded DDL bootstrap unless the
    /// caller has disabled it via
    /// <see cref="EdictPostgresPersistenceOptions.BootstrapSchema"/>.
    /// </summary>
    public static ISiloBuilder AddEdictPostgresPersistence(
        this ISiloBuilder silo,
        Action<EdictPostgresPersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new EdictPostgresPersistenceOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "AddEdictPostgresPersistence requires EdictPostgresPersistenceOptions.ConnectionString.");
        }

        silo.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();

        if (options.BootstrapSchema)
        {
            PostgresDdlBootstrap.Run(options.ConnectionString);
        }

        // Edict.Postgres ships its own grain-storage provider rather than
        // chaining Orleans 10's AdoNetGrainStorage. The shipped provider
        // hard-codes the literal "state" as the row-key discriminator
        // (https://github.com/dotnet/orleans/issues/9737), so every Grain<T>
        // sharing a grain id — the command handler and any per-aggregate
        // projection grain on the same RouteKey — collapses into one row
        // and races on ETag. EdictPostgresGrainStorage keys on
        // (grain_type, grain_id, state_name, service_id) instead so
        // concept-level grains stay distinct. Do not swap this for
        // AddAdoNetGrainStorage until the upstream issue is resolved.
        var grainStorageProviderName = options.GrainStorageProviderName;
        silo.Services.AddKeyedSingleton<IGrainStorage>(grainStorageProviderName, (sp, _) =>
        {
            var clusterOptions = sp.GetRequiredService<IOptions<ClusterOptions>>().Value;
            return new EdictPostgresGrainStorage(
                options.ConnectionString,
                clusterOptions.ServiceId,
                sp.GetRequiredService<Serializer>(),
                sp,
                sp.GetRequiredService<ILogger<EdictPostgresGrainStorage>>());
        });
        // PubSubStore stays on Orleans' shipped AdoNet provider — its grain
        // type is Orleans-internal (PubSubRendezvousGrain) and no other
        // grain type shares its key shape, so the dotnet/orleans#9737 row
        // collision does not bite here.
        silo.AddAdoNetGrainStorage("PubSubStore", opt =>
        {
            opt.Invariant = options.Invariant;
            opt.ConnectionString = options.ConnectionString;
        });

        silo.UseAdoNetReminderService(opt =>
        {
            opt.Invariant = options.Invariant;
            opt.ConnectionString = options.ConnectionString;
        });

        var connectionString = options.ConnectionString;
        var deadLetterTable = options.DeadLetterTableName;
        var claimCheckTable = options.ClaimCheckTableName;

        silo.Services.AddSingleton<IEdictTableStoreFactory>(sp =>
            new PostgresTableWriteStoreFactory(
                connectionString,
                sp.GetRequiredService<Serializer>(),
                sp));

        silo.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(sp =>
            new PostgresTableRepository<EdictDeadLetterEntry>(
                connectionString, deadLetterTable, sp.GetRequiredService<Serializer>()));

        silo.Services.TryAddSingleton<IEdictClaimCheckStore>(
            new PostgresClaimCheckStore(connectionString, claimCheckTable));

        return silo;
    }
}
