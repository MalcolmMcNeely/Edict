using Edict.Contracts.Commands;
using Edict.Core.Commands;

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
        "Edict.Core.DeadLetter.EdictDeadLetterProjectionBuilder",
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
