using System.ComponentModel;
using System.Reflection;

using Edict.Azure.Persistence.TableStorage;
using Edict.Azure.Streaming;
using Edict.Contracts.Commands;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Kafka;
using Edict.Postgres;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Edict.Architecture.Tests;

// Every public top-level type in a framework assembly is on a hand-maintained
// allow-list. A new public type therefore fails CI until the maintainer either
// flips it to internal or extends the allow-list — forcing the ADR-0017
// brand-rule conversation at PR review rather than relying on review of any
// individual PR.
public class PublicSurfaceAllowListTests
{
    [Fact]
    public void EdictCore_ExceptionsHaveEdictPrefix()
    {
        var coreAssembly = typeof(EdictSender).Assembly;
        var offenders = coreAssembly
            .GetExportedTypes()
            .Where(type => !type.IsNested)
            .Where(type => typeof(Exception).IsAssignableFrom(type))
            .Where(type => type.Name.EndsWith("Exception", StringComparison.Ordinal))
            .Where(type => !type.Name.StartsWith("Edict", StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Public exception types in Edict.Core must be Edict-prefixed:\n  - "
                + string.Join("\n  - ", offenders));
    }

    [Fact]
    public void EdictContracts_PublicTypesMatchAllowList()
    {
        var contractsAssembly = typeof(EdictCommand).Assembly;
        var actual = contractsAssembly
            .GetExportedTypes()
            .Where(type => !type.IsNested)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var unexpected = actual.Where(name => !EdictContractsAllowList.Contains(name)).ToList();
        var missing = EdictContractsAllowList.Where(name => !actual.Contains(name)).ToList();

        Assert.True(
            unexpected.Count == 0 && missing.Count == 0,
            BuildDriftMessage(unexpected, missing));
    }

    [Fact]
    public void EdictCore_GeneratorOnlyAndFrameworkInternalMembers_AreHiddenFromIntelliSense()
    {
        var addEdictServiceCollectionOverload = typeof(EdictServiceCollectionExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method.Name == nameof(EdictServiceCollectionExtensions.AddEdict)
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(IServiceCollection));

        var targets = new (string Description, MemberInfo Member)[]
        {
            ("EdictSender (class)", typeof(EdictSender)),
            (
                "EdictSender.SendFastPathAsync<TCommand>",
                typeof(EdictSender).GetMethod(nameof(EdictSender.SendFastPathAsync))!),
            (
                "EdictCommandHandler<TState>.RaiseFast<TEvent>",
                typeof(EdictCommandHandler<>).GetMethod("RaiseFast")!),
            (
                "OutboxServiceCollectionExtensions.AddEdictOutbox",
                typeof(OutboxServiceCollectionExtensions).GetMethod(nameof(OutboxServiceCollectionExtensions.AddEdictOutbox))!),
            (
                "EdictServiceCollectionExtensions.AddEdict(IServiceCollection)",
                addEdictServiceCollectionOverload),
        };

        var missing = targets
            .Where(target =>
            {
                var attribute = target.Member.GetCustomAttribute<EditorBrowsableAttribute>();
                return attribute?.State != EditorBrowsableState.Never;
            })
            .Select(target => target.Description)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These framework-internal and generator-only members must carry [EditorBrowsable(EditorBrowsableState.Never)]:\n  - "
                + string.Join("\n  - ", missing));
    }

    [Fact]
    public void EdictCore_PublicTypesMatchAllowList()
    {
        var coreAssembly = typeof(EdictSender).Assembly;
        var actual = coreAssembly
            .GetExportedTypes()
            .Where(type => !type.IsNested)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var unexpected = actual.Where(name => !EdictCoreAllowList.Contains(name)).ToList();
        var missing = EdictCoreAllowList.Where(name => !actual.Contains(name)).ToList();

        Assert.True(
            unexpected.Count == 0 && missing.Count == 0,
            BuildDriftMessage(unexpected, missing));
    }

    [Fact]
    public void EdictAzurePersistence_PublicTypesMatchAllowList()
    {
        var azurePersistenceAssembly = typeof(AzureTableRepository<>).Assembly;
        var actual = azurePersistenceAssembly
            .GetExportedTypes()
            .Where(type => !type.IsNested)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var unexpected = actual.Where(name => !EdictAzurePersistenceAllowList.Contains(name)).ToList();
        var missing = EdictAzurePersistenceAllowList.Where(name => !actual.Contains(name)).ToList();

        Assert.True(
            unexpected.Count == 0 && missing.Count == 0,
            BuildDriftMessage(unexpected, missing));
    }

    [Fact]
    public void EdictAzureStreaming_PublicTypesMatchAllowList()
    {
        var azureStreamingAssembly = typeof(EdictAzureStreamsOptions).Assembly;
        var actual = azureStreamingAssembly
            .GetExportedTypes()
            .Where(type => !type.IsNested)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var unexpected = actual.Where(name => !EdictAzureStreamingAllowList.Contains(name)).ToList();
        var missing = EdictAzureStreamingAllowList.Where(name => !actual.Contains(name)).ToList();

        Assert.True(
            unexpected.Count == 0 && missing.Count == 0,
            BuildDriftMessage(unexpected, missing));
    }

    [Fact]
    public void EdictPostgres_PublicTypesMatchAllowList()
    {
        var postgresAssembly = typeof(EdictPostgresSiloBuilderExtensions).Assembly;
        var actual = postgresAssembly
            .GetExportedTypes()
            .Where(type => !type.IsNested)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var unexpected = actual.Where(name => !EdictPostgresAllowList.Contains(name)).ToList();
        var missing = EdictPostgresAllowList.Where(name => !actual.Contains(name)).ToList();

        Assert.True(
            unexpected.Count == 0 && missing.Count == 0,
            BuildDriftMessage(unexpected, missing));
    }

    [Fact]
    public void EdictKafka_PublicTypesMatchAllowList()
    {
        var kafkaAssembly = typeof(EdictKafkaSiloBuilderExtensions).Assembly;
        var actual = kafkaAssembly
            .GetExportedTypes()
            .Where(type => !type.IsNested)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var unexpected = actual.Where(name => !EdictKafkaAllowList.Contains(name)).ToList();
        var missing = EdictKafkaAllowList.Where(name => !actual.Contains(name)).ToList();

        Assert.True(
            unexpected.Count == 0 && missing.Count == 0,
            BuildDriftMessage(unexpected, missing));
    }

    static readonly HashSet<string> EdictContractsAllowList = new(StringComparer.Ordinal)
    {
        "Edict.Contracts.ClaimCheck.EdictEnvelopeOverflowException",
        "Edict.Contracts.Commands.EdictCommand",
        "Edict.Contracts.Commands.EdictCommandResult",
        "Edict.Contracts.Commands.EdictRejectionReason",
        "Edict.Contracts.Commands.EdictRouteKeyAttribute",
        "Edict.Contracts.Configuration.EdictOptions",
        "Edict.Contracts.Configuration.EdictPersistenceProviderMarker",
        "Edict.Contracts.Configuration.EdictStreamsProviderMarker",
        "Edict.Contracts.Configuration.IEdictWiringMarker",
        "Edict.Contracts.DeadLetter.EdictDeadLetterEntry",
        "Edict.Contracts.DeadLetter.EdictDeadLetterFailureKind",
        "Edict.Contracts.DeadLetter.EdictDeadLetterRaised",
        "Edict.Contracts.DeadLetter.IEdictDeadLetterRepository",
        "Edict.Contracts.EdictUnit",
        "Edict.Contracts.Events.EdictEvent",
        "Edict.Contracts.Events.EdictEventEnvelope",
        "Edict.Contracts.Events.EdictStreamAttribute",
        "Edict.Contracts.Persistence.IEdictPersistedState",
        "Edict.Contracts.Routing.EdictEventStreamAccessor",
        "Edict.Contracts.Routing.EdictEventStreamsAttribute",
        "Edict.Contracts.Routing.EdictEventTagWritersAttribute",
        "Edict.Contracts.Routing.EdictRoutesAttribute",
        "Edict.Contracts.Sending.IEdictSender",
        "Edict.Contracts.TableStorage.IEdictTableRepository`1",
        "Edict.Contracts.TableStorage.IEdictTableWriteStore`1",
        "Edict.Contracts.Telemetry.EdictTelemeterizedAttribute",
    };

    static readonly HashSet<string> EdictCoreAllowList = new(StringComparer.Ordinal)
    {
        "Edict.Core.Commands.CommandRoute",
        "Edict.Core.Commands.EdictCommandHandler",
        "Edict.Core.Commands.EdictCommandHandler`1",
        "Edict.Core.Commands.EdictSender",
        "Edict.Core.Commands.EdictUnroutableCommandException",
        "Edict.Core.Commands.IEdictCommandHandler",
        "Edict.Core.Configuration.EdictWiringException",
        "Edict.Core.DeadLetter.EdictClaimCheckFetchException",
        "Edict.Core.DeadLetter.EdictDeadLetterTable",
        "Edict.Core.DeadLetter.EdictInternalInvariantException",
        "Edict.Core.DeadLetter.EdictSagaCoordinationException",
        "Edict.Core.DeadLetter.EdictUnregisteredTypeException",
        "Edict.Core.EdictServiceCollectionExtensions",
        "Edict.Core.EdictSiloBuilderExtensions",
        "Edict.Core.EventHandler.EdictEventHandler",
        "Edict.Core.Idempotency.EdictIdempotencyBase",
        "Edict.Core.Idempotency.EdictIdempotencyBase`1",
        "Edict.Core.Idempotency.IEdictEventConsumer",
        "Edict.Core.Idempotency.IdempotencyState", // ADR 0045: persisted-state slot on GrainEnvelope<TPayload> — permanent resident.
        "Edict.Core.Metrics.IEdictMetricsCache",
        "Edict.Core.Outbox.GrainEnvelope`1", // ADR 0045: base chain of EdictCommandHandler<TState> / EdictIdempotencyBase<TPayload> — permanent resident (CS9338).
        "Edict.Core.Outbox.OutboxEffectKind", // ADR 0045: persisted-state slot on GrainEnvelope<TPayload> — permanent resident.
        "Edict.Core.Outbox.OutboxEntry", // ADR 0045: persisted-state slot on GrainEnvelope<TPayload> — permanent resident.
        "Edict.Core.Outbox.OutboxServiceCollectionExtensions",
        "Edict.Core.Outbox.OutboxSlice", // ADR 0045: persisted-state slot on GrainEnvelope<TPayload> — permanent resident.
        "Edict.Core.Outbox.UpsertRowEffect", // ADR 0045: persisted-state slot on GrainEnvelope<TPayload> — permanent resident.
        "Edict.Core.Projections.EdictProjectionBuilder",
        "Edict.Core.Projections.EdictTableProjectionBuilder`1",
        "Edict.Core.Projections.IEdictProjectionBuilder",
        "Edict.Core.Sagas.EdictSaga`1",
        "Edict.Core.Sagas.IEdictSaga",
        "Edict.Core.Serialization.EdictSerialization",
        "Edict.Core.TableStorage.IEdictTableStoreFactory", // ADR 0045: ctor param of consumer-typed EdictTableProjectionBuilder<T> — permanent resident.
        "OrleansCodeGen.Edict.Core.Commands.Codec_Invokable_IEdictCommandHandler_GrainReference_E0958B40",
        "OrleansCodeGen.Edict.Core.Commands.Copier_Invokable_IEdictCommandHandler_GrainReference_E0958B40",
        "OrleansCodeGen.Edict.Core.Commands.Invokable_IEdictCommandHandler_GrainReference_E0958B40",
        "OrleansCodeGen.Edict.Core.Idempotency.Codec_IdempotencyState",
        "OrleansCodeGen.Edict.Core.Idempotency.Codec_Invokable_IEdictEventConsumer_GrainReference_AE8589E1",
        "OrleansCodeGen.Edict.Core.Idempotency.Copier_IdempotencyState",
        "OrleansCodeGen.Edict.Core.Idempotency.Copier_Invokable_IEdictEventConsumer_GrainReference_AE8589E1",
        "OrleansCodeGen.Edict.Core.Idempotency.Invokable_IEdictEventConsumer_GrainReference_AE8589E1",
        "OrleansCodeGen.Edict.Core.Outbox.Codec_GrainEnvelope`1",
        "OrleansCodeGen.Edict.Core.Outbox.Codec_OutboxEntry",
        "OrleansCodeGen.Edict.Core.Outbox.Codec_OutboxSlice",
        "OrleansCodeGen.Edict.Core.Outbox.Codec_UpsertRowEffect",
        "OrleansCodeGen.Edict.Core.Outbox.Copier_GrainEnvelope`1",
        "OrleansCodeGen.Edict.Core.Outbox.Copier_OutboxEntry",
        "OrleansCodeGen.Edict.Core.Outbox.Copier_OutboxSlice",
        "OrleansCodeGen.Edict.Core.Outbox.Copier_UpsertRowEffect",
        "OrleansCodeGen.Edict.Core.Sagas.Codec_Invokable_IEdictSaga_GrainReference_747818AD",
        "OrleansCodeGen.Edict.Core.Sagas.Copier_Invokable_IEdictSaga_GrainReference_747818AD",
        "OrleansCodeGen.Edict.Core.Sagas.Invokable_IEdictSaga_GrainReference_747818AD",
    };

    static readonly HashSet<string> EdictAzurePersistenceAllowList = new(StringComparer.Ordinal)
    {
        "Edict.Azure.Persistence.EdictAzurePersistenceOptions",
        "Edict.Azure.Persistence.EdictAzurePersistenceSiloBuilderExtensions",
        "Edict.Azure.Persistence.TableStorage.AzureTableRepository`1",
    };

    static readonly HashSet<string> EdictAzureStreamingAllowList = new(StringComparer.Ordinal)
    {
        "Edict.Azure.Streaming.ClaimCheck.EdictAzureBlobClaimCheckOptions",
        "Edict.Azure.Streaming.EdictAzureStreamingSiloBuilderExtensions",
        "Edict.Azure.Streaming.EdictAzureStreamsOptions",
    };

    static readonly HashSet<string> EdictPostgresAllowList = new(StringComparer.Ordinal)
    {
        "Edict.Postgres.EdictPostgresPersistenceOptions",
        "Edict.Postgres.EdictPostgresSiloBuilderExtensions",
        "Edict.Postgres.EdictPostgresStorageException",
        "Edict.Postgres.TableStorage.PostgresTableRepository`1",
        "OrleansCodeGen.Edict.Postgres.Codec_EdictPostgresStorageException",
        "OrleansCodeGen.Edict.Postgres.Copier_EdictPostgresStorageException",
    };

    static readonly HashSet<string> EdictKafkaAllowList = new(StringComparer.Ordinal)
    {
        "Edict.Kafka.EdictKafkaSiloBuilderExtensions",
        "Edict.Kafka.EdictKafkaStreamsOptions",
    };

    static string BuildDriftMessage(IReadOnlyList<string> unexpected, IReadOnlyList<string> missing)
    {
        var sections = new List<string>();
        if (unexpected.Count > 0)
        {
            sections.Add("Public types not on the allow-list (either flip to internal or extend the list):\n  - "
                + string.Join("\n  - ", unexpected));
        }
        if (missing.Count > 0)
        {
            sections.Add("Allow-list entries that no longer exist in the assembly (remove from the list):\n  - "
                + string.Join("\n  - ", missing));
        }
        return string.Join("\n\n", sections);
    }
}
