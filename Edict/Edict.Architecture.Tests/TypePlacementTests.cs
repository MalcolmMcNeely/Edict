using ArchUnitNET.Loader;
using System.Reflection;
using ArchUnitNET.xUnit;

using Edict.Azure.Persistence;
using Edict.Azure.Persistence.TableStorage;
using Edict.Azure.Streaming;
using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.TableStorage;
using Edict.Core.Idempotency;
using Edict.Core.Projections;
using Edict.Kafka;
using Edict.Postgres;
using Edict.Postgres.TableStorage;
using Edict.Substrate;
using Edict.Substrate.Azurite;
using Edict.Substrate.KafkaPostgres;

using Sample.Contracts.Orders.Commands;

using Xunit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using DomainArchitecture = ArchUnitNET.Domain.Architecture;

namespace Edict.Architecture.Tests;

public class TypePlacementTests
{
    static readonly DomainArchitecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(AzureTableWriteStoreFactory).Assembly,
            typeof(EdictAzureStreamsOptions).Assembly,
            typeof(EdictCommand).Assembly,
            typeof(EdictIdempotencyBase).Assembly,
            typeof(ISubstrate).Assembly,
            typeof(AzuriteSubstrate).Assembly,
            typeof(KafkaPostgresSubstrate).Assembly,
            typeof(EdictPostgresPersistenceOptions).Assembly,
            typeof(EdictKafkaStreamsOptions).Assembly,
            typeof(PlaceOrderCommand).Assembly)
        .Build();

    [Fact]
    public void EdictEvent_ShouldResideInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^EdictEvent$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Events$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictStreamAttribute_ShouldResideInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^EdictStreamAttribute$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Events$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictRouteKeyAttribute_ShouldResideInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^EdictRouteKeyAttribute$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Commands$");

        rule.Check(Architecture);
    }

    [Fact]
    public void IEdictTableRepository_ShouldResideInEdictContracts()
    {
        var rule = Interfaces().That().HaveNameStartingWith("IEdictTableRepository")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.TableStorage$");

        rule.Check(Architecture);
    }

    [Fact]
    public void IEdictTableWriteStore_ShouldResideInEdictContracts()
    {
        var rule = Interfaces().That().HaveNameStartingWith("IEdictTableWriteStore")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.TableStorage$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictIdempotencyBase_ShouldResideInEdictCoreIdempotency()
    {
        var rule = Classes().That().HaveNameMatching("^EdictIdempotencyBase$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Idempotency$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictProjectionBuilder_ShouldResideInEdictCore()
    {
        var rule = Classes().That().HaveNameMatching("^EdictProjectionBuilder$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Projections$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictTableProjectionBuilder_ShouldResideInEdictCore()
    {
        var rule = Classes().That().HaveNameStartingWith("EdictTableProjectionBuilder")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Projections$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictSaga_ShouldResideInEdictCoreSagas()
    {
        // Exception types live alongside the other dead-letter-runtime types,
        // not with consumer saga bases.
        var rule = Classes().That().HaveNameStartingWith("EdictSaga")
            .And().DoNotHaveNameEndingWith("Exception")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Sagas$");

        rule.Check(Architecture);
    }

    [Fact]
    public void IEdictSaga_ShouldResideInEdictCoreSagas()
    {
        var rule = Interfaces().That().HaveNameMatching("^IEdictSaga$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Sagas$");

        rule.Check(Architecture);
    }

    [Fact]
    public void IEdictEventConsumer_ShouldResideInEdictCoreIdempotency()
    {
        var rule = Interfaces().That().HaveNameMatching("^IEdictEventConsumer$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Idempotency$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictUnit_ShouldResideInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^EdictUnit$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts$");

        rule.Check(Architecture);
    }

    [Fact]
    public void OutboxTypes_ShouldResideInEdictCoreOutbox()
    {
        var rule = Types().That()
            .HaveNameMatching("^(OutboxEntry|OutboxSlice|OutboxEffectKind|OutboxBackoff|GrainEnvelope)")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Outbox$");

        rule.Check(Architecture);
    }

    [Fact]
    public void IEdictDeadLetterRepository_ShouldResideInEdictContracts()
    {
        var rule = Interfaces().That().HaveNameStartingWith("IEdictDeadLetterRepository")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.DeadLetter$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictDeadLetterEntry_ShouldResideInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^EdictDeadLetterEntry$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.DeadLetter$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictOptions_ShouldResideInEdictContractsConfiguration()
    {
        var rule = Types().That().HaveNameMatching("^EdictOptions$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Configuration$");

        rule.Check(Architecture);
    }

    [Fact]
    public void OutboxAndDeadLetterEngine_ShouldBeBareNamed_NoConsumerTypesIt()
    {
        // EdictDeadLetterTable is excluded: it's the stable public anchor for
        // the dead-letter table's name — consumers type its const at their
        // call site, earning the Edict prefix per brand-rule clause (a).
        // EdictDeadLetterProjectionBuilder is excluded: legacy brand on the
        // (now internal) auto-wired grain class; dropping the prefix is a
        // separate rename outside this slice's scope.
        // Edict*Exception types are excluded: a consumer can catch them, which
        // is what earns them the Edict prefix even though they sit beside the
        // bare engine.
        var rule = Types().That()
            .ResideInNamespaceMatching(@"^Edict\.Core\.(Outbox|DeadLetter)$")
            .And().DoNotHaveNameStartingWith("EdictDeadLetterProjectionBuilder")
            .And().DoNotHaveNameStartingWith("EdictDeadLetterTable")
            .And().DoNotHaveNameEndingWith("Exception")
            .Should().HaveNameMatching("^(?!Edict)")
            .AndShould().HaveNameMatching("^(?!IEdict)");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictDeadLetterProjectionBuilder_ShouldResideInEdictCoreDeadLetter()
    {
        var rule = Classes().That().HaveNameStartingWith("EdictDeadLetterProjectionBuilder")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.DeadLetter$");

        rule.Check(Architecture);
    }

    [Fact]
    public void OutboxHost_ShouldResideInEdictCoreOutboxAsInternalBareNamed()
    {
        var rule = Classes().That().HaveNameStartingWith("OutboxHost")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Outbox$")
            .AndShould().NotBePublic()
            .AndShould().HaveNameMatching("^(?!Edict)");

        rule.Check(Architecture);
    }

    [Fact]
    public void OutboxDrainEngine_ShouldNotExist()
    {
        // Algorithm folded into OutboxHost; the engine/host split is gone.
        var coreAssembly = typeof(EdictIdempotencyBase).Assembly;
        var match = coreAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "OutboxDrainEngine");
        Assert.Null(match);
    }

    [Fact]
    public void IOutboxHost_ShouldNotExist()
    {
        // The interface that served only OutboxDrainEngine's testability is
        // gone now that the host *is* the testable thing.
        var coreAssembly = typeof(EdictIdempotencyBase).Assembly;
        var match = coreAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "IOutboxHost");
        Assert.Null(match);
    }

    [Fact]
    public void EdictDurableConsumerBase_ShouldNotExist()
    {
        // The intermediate shared root is gone; each consumer-facing root
        // owns its own ~30-40 line lifecycle shell.
        var coreAssembly = typeof(EdictIdempotencyBase).Assembly;
        var match = coreAssembly.GetTypes()
            .FirstOrDefault(t => t.Name.StartsWith("EdictDurableConsumerBase", StringComparison.Ordinal));
        Assert.Null(match);
    }

    [Fact]
    public void ConsumerFacingRoots_ShouldNotExposeOutboxHostInPublicOrProtectedSurface()
    {
        // OutboxHost is a composed internal — no consumer surface (public OR
        // protected, since protected is reachable by consumer subclasses)
        // should leak the type. The framework bases hold it as a private
        // field; test probes can reach the underlying envelope via the
        // internal OutboxStateForProbe accessor.
        var coreAssembly = typeof(EdictIdempotencyBase).Assembly;
        var leakingMembers = coreAssembly.GetExportedTypes()
            .SelectMany(t =>
                t.GetProperties(BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance)
                    .Where(p =>
                        (p.GetMethod is { IsFamily: true } or { IsFamilyOrAssembly: true } or { IsPublic: true })
                        && p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition().Name.StartsWith("OutboxHost", StringComparison.Ordinal))
                    .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        Assert.Empty(leakingMembers);
    }

    [Fact]
    public void EdictEventHandler_ShouldResideInEdictCoreEventHandler()
    {
        var rule = Classes().That().HaveNameMatching("^EdictEventHandler$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.EventHandler$");

        rule.Check(Architecture);
    }

    [Fact]
    public void InvokeHandlerExecutor_ShouldBeInternalAndBareNamed()
    {
        var rule = Classes().That().HaveNameMatching("^InvokeHandlerExecutor$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.EventHandler$")
            .AndShould().NotBePublic()
            .AndShould().HaveNameMatching("^(?!Edict)");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictEventEnvelope_ShouldResideInEdictContractsEvents()
    {
        var rule = Types().That().HaveNameMatching("^EdictEventEnvelope$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Events$");

        rule.Check(Architecture);
    }

    [Fact]
    public void IEdictClaimCheckStore_ShouldResideInEdictContractsClaimCheck()
    {
        var rule = Interfaces().That().HaveNameMatching("^IEdictClaimCheckStore$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.ClaimCheck$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictEnvelopeOverflowException_ShouldResideInEdictContractsClaimCheck()
    {
        var rule = Types().That().HaveNameMatching("^EdictEnvelopeOverflowException$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.ClaimCheck$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictDeadLetterFailureKind_ShouldResideInEdictContractsDeadLetter()
    {
        var rule = Types().That().HaveNameMatching("^EdictDeadLetterFailureKind$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.DeadLetter$");

        rule.Check(Architecture);
    }

    [Fact]
    public void ClaimCheckPolicy_ShouldResideInEdictCoreClaimCheck()
    {
        var rule = Classes().That().HaveNameMatching("^ClaimCheckPolicy$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.ClaimCheck$");

        rule.Check(Architecture);
    }

    [Fact]
    public void AzureBlobClaimCheckStore_ShouldResideInEdictAzureStreamingClaimCheck()
    {
        var rule = Classes().That().HaveNameMatching("^AzureBlobClaimCheckStore$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure\.Streaming\.ClaimCheck$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictAzureStreamsOptions_ShouldResideInEdictAzureStreaming()
    {
        var rule = Classes().That().HaveNameMatching("^EdictAzureStreamsOptions$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure\.Streaming$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictKafkaStreamsOptions_ShouldResideInEdictKafka()
    {
        var rule = Classes().That().HaveNameMatching("^EdictKafkaStreamsOptions$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Kafka$");

        rule.Check(Architecture);
    }

    // The Kafka consumer surface is AddEdictKafkaStreams + EdictKafkaStreamsOptions
    // (+ EdictKafkaWireEnvelope, which Orleans' [GenerateSerializer] requires to be
    // public). Everything else under Edict.Kafka.Internal is mechanism and must
    // not leak: a consumer reaching into the adapter, receiver, or topic
    // provisioner would couple to internals the framework is free to reshape.
    [Fact]
    public void EdictKafkaInternals_ShouldNotBePublic()
    {
        var kafkaAssembly = typeof(EdictKafkaStreamsOptions).Assembly;
        var leakingTypes = kafkaAssembly.GetExportedTypes()
            .Where(t => t.Namespace is not null
                && t.Namespace.StartsWith("Edict.Kafka.Internal", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToList();

        Assert.Empty(leakingTypes);
    }

    // Topology: one Kafka topic per [EdictStream]. The
    // EdictKafkaPartitionMapper carries the topology through the opaque
    // Orleans QueueId via two static decode helpers — TopicFor and
    // PartitionFor. A regression to a single-topic hardcode would either
    // drop TopicFor entirely (no topic to decode if there is only one) or
    // hide the topic in a field not surfaced as a static helper. Either
    // way, this fact would fail.
    [Fact]
    public void EdictKafkaPartitionMapper_ShouldExposeTopicAndPartitionDecodeHelpers()
    {
        var kafkaAssembly = typeof(EdictKafkaStreamsOptions).Assembly;
        var mapper = kafkaAssembly.GetType("Edict.Kafka.Internal.EdictKafkaPartitionMapper")
            ?? throw new InvalidOperationException("EdictKafkaPartitionMapper not found in Edict.Kafka.");

        var topicFor = mapper.GetMethod(
            "TopicFor",
            BindingFlags.Public | BindingFlags.Static);
        var partitionFor = mapper.GetMethod(
            "PartitionFor",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(topicFor);
        Assert.NotNull(partitionFor);
        Assert.Equal(typeof(string), topicFor!.ReturnType);
        Assert.Equal(typeof(int), partitionFor!.ReturnType);
    }

    // The stream registry is the discovery seam for the per-stream topology;
    // without it the provisioner cannot ensure topics at startup and the
    // mapper cannot enumerate queues. Pinning its placement here means a
    // refactor that moves the seam under Edict.Kafka (a public namespace) or
    // back into Edict.Core would have to update this fact deliberately.
    [Fact]
    public void EdictKafkaStreamRegistry_ShouldResideInEdictKafkaInternal()
    {
        var kafkaAssembly = typeof(EdictKafkaStreamsOptions).Assembly;
        var registry = kafkaAssembly.GetType("Edict.Kafka.Internal.EdictKafkaStreamRegistry");

        Assert.NotNull(registry);
        Assert.False(registry!.IsPublic, "EdictKafkaStreamRegistry must remain internal.");
    }

    [Fact]
    public void EdictAzurePersistenceOptions_ShouldResideInEdictAzurePersistence()
    {
        var rule = Classes().That().HaveNameMatching("^EdictAzurePersistenceOptions$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure\.Persistence$");

        rule.Check(Architecture);
    }

    [Fact]
    public void AzureTableRepository_ShouldResideInEdictAzurePersistence()
    {
        var rule = Classes().That().HaveNameStartingWith("AzureTableRepository")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure\.Persistence\.TableStorage$");

        rule.Check(Architecture);
    }

    [Fact]
    public void AzureTableWriteStoreFactory_ShouldResideInEdictAzurePersistence()
    {
        var rule = Classes().That().HaveNameMatching("^AzureTableWriteStoreFactory$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure\.Persistence\.TableStorage$");

        rule.Check(Architecture);
    }

    [Fact]
    public void ISubstrate_ShouldResideInEdictSubstrate()
    {
        var rule = Interfaces().That().HaveNameMatching("^ISubstrate$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Substrate$");

        rule.Check(Architecture);
    }

    [Fact]
    public void ISubstrateRuntime_ShouldResideInEdictSubstrate()
    {
        var rule = Interfaces().That().HaveNameMatching("^ISubstrateRuntime$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Substrate$");

        rule.Check(Architecture);
    }

    [Fact]
    public void AzuriteSubstrate_ShouldResideInEdictSubstrateAzurite()
    {
        var rule = Classes().That().HaveNameMatching("^AzuriteSubstrate$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Substrate\.Azurite$");

        rule.Check(Architecture);
    }

    [Fact]
    public void KafkaPostgresSubstrate_ShouldResideInEdictSubstrateKafkaPostgres()
    {
        var rule = Classes().That().HaveNameMatching("^KafkaPostgresSubstrate$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Substrate\.KafkaPostgres$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictPostgresPersistenceOptions_ShouldResideInEdictPostgres()
    {
        var rule = Classes().That().HaveNameMatching("^EdictPostgresPersistenceOptions$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Postgres$");

        rule.Check(Architecture);
    }

    [Fact]
    public void PostgresTableRepository_ShouldResideInEdictPostgres()
    {
        var rule = Classes().That().HaveNameStartingWith("PostgresTableRepository")
            .Should().ResideInNamespaceMatching(@"^Edict\.Postgres\.TableStorage$");

        rule.Check(Architecture);
    }

    [Fact]
    public void PostgresTableWriteStoreFactory_ShouldResideInEdictPostgres()
    {
        var rule = Classes().That().HaveNameMatching("^PostgresTableWriteStoreFactory$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Postgres\.TableStorage$");

        rule.Check(Architecture);
    }

    [Fact]
    public void PostgresClaimCheckStore_ShouldResideInEdictPostgresClaimCheck()
    {
        var rule = Classes().That().HaveNameMatching("^PostgresClaimCheckStore$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Postgres\.ClaimCheck$");

        rule.Check(Architecture);
    }
}
