using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;

using Edict.Azure.TableStorage;
using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.TableStorage;
using Edict.Core.Idempotency;
using Edict.Core.Projections;

using Sample.Contracts.Orders.Commands;

using Xunit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using DomainArchitecture = ArchUnitNET.Domain.Architecture;

namespace Edict.Architecture.Tests;

public class TypePlacementTests
{
    private static readonly DomainArchitecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(AzureTableWriteStoreFactory).Assembly,
            typeof(EdictCommand).Assembly,
            typeof(EdictIdempotencyBase).Assembly,
            typeof(PlaceOrderCommand).Assembly)
        .Build();

    // Contracts: Event, [Stream], [RouteKey], ITableRepository — ADR 0008 / ADR 0012

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

    // Core runtime: EdictIdempotencyBase, EdictProjectionBuilder, EdictTableProjectionBuilder — ADR 0008 / ADR 0017

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

    // Saga: EdictSaga<TProgress> + IEdictSaga are consumer-facing (brand-prefixed)
    // and live in Edict.Core/Saga/; the dispatch-buffer mechanism is bare. ADR 0020.

    [Fact]
    public void EdictSaga_ShouldResideInEdictCoreSaga()
    {
        var rule = Classes().That().HaveNameStartingWith("EdictSaga")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Saga$");

        rule.Check(Architecture);
    }

    [Fact]
    public void IEdictSaga_ShouldResideInEdictCoreSaga()
    {
        var rule = Interfaces().That().HaveNameMatching("^IEdictSaga$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Saga$");

        rule.Check(Architecture);
    }

    // The shared brand-prefixed grain-interface root for every event-consuming
    // grain (sagas, projection builders) — brand clause (b), co-located with
    // the base that implements it (#53).
    [Fact]
    public void IEdictEventConsumer_ShouldResideInEdictCoreIdempotency()
    {
        var rule = Interfaces().That().HaveNameMatching("^IEdictEventConsumer$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Idempotency$");

        rule.Check(Architecture);
    }

    // EdictUnit: the stateless-payload shim type — consumer-visible surface, ADR 0008 / 0017

    [Fact]
    public void EdictUnit_ShouldResideInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^EdictUnit$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts$");

        rule.Check(Architecture);
    }

    // Outbox/DeadLetter engine: ADR 0018 / 0019 — folders Outbox/ and DeadLetter/,
    // bare-named (no consumer types it; the engine stays internal).

    [Fact]
    public void OutboxTypes_ShouldResideInEdictCoreOutbox()
    {
        var rule = Types().That()
            .HaveNameMatching("^(OutboxEntry|OutboxSlice|OutboxEffectKind|OutboxBackoff|GrainEnvelope)")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Outbox$");

        rule.Check(Architecture);
    }

    // ADR 0022 consumer surface: the read-only repository + its DTO are
    // consumer-facing, so they are brand-prefixed and live in Edict.Contracts
    // (mirroring IEdictTableRepository). The in-grain dead-letter slice, its
    // operator-recovery admin grain interface, and the saturated-intake
    // exception are all removed under ADR 0022 — no placement tests for them.

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
        // EdictDeadLetterProjectionBuilder is the deliberate brand-prefixed
        // exception: the framework-shipped projection grain whose role-named
        // subclass naturally inherits the brand from EdictTableProjectionBuilder
        // (ADR 0022 — auto-wired by AddEdict()). The pre-refactor
        // EdictDurableConsumerBase exception is gone (#69 — composition).
        var rule = Types().That()
            .ResideInNamespaceMatching(@"^Edict\.Core\.(Outbox|DeadLetter)$")
            .And().DoNotHaveNameStartingWith("EdictDeadLetterProjectionBuilder")
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

    // #69: composition refactor — the intermediate base, the host seam, and
    // the standalone drain engine are gone. The replacement OutboxHost lives
    // as a field on each consumer-facing root.

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
        var coreAssembly = typeof(Edict.Core.Idempotency.EdictIdempotencyBase).Assembly;
        var match = coreAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "OutboxDrainEngine");
        Assert.Null(match);
    }

    [Fact]
    public void IOutboxHost_ShouldNotExist()
    {
        // The interface that served only OutboxDrainEngine's testability is
        // gone now that the host *is* the testable thing.
        var coreAssembly = typeof(Edict.Core.Idempotency.EdictIdempotencyBase).Assembly;
        var match = coreAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "IOutboxHost");
        Assert.Null(match);
    }

    [Fact]
    public void EdictDurableConsumerBase_ShouldNotExist()
    {
        // The intermediate shared root is gone; each consumer-facing root
        // owns its own ~30-40 line lifecycle shell.
        var coreAssembly = typeof(Edict.Core.Idempotency.EdictIdempotencyBase).Assembly;
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
        var coreAssembly = typeof(Edict.Core.Idempotency.EdictIdempotencyBase).Assembly;
        var leakingMembers = coreAssembly.GetExportedTypes()
            .SelectMany(t =>
                t.GetProperties(System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                    .Where(p =>
                        (p.GetMethod is { IsFamily: true } or { IsFamilyOrAssembly: true } or { IsPublic: true })
                        && p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition().Name.StartsWith("OutboxHost", StringComparison.Ordinal))
                    .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        Assert.Empty(leakingMembers);
    }

    // Azure provider: AzureTableRepository, AzureTableWriteStoreFactory — ADR 0014

    // EdictEventHandler: ADR 0023 — the consumer-facing terminal side-effect
    // base lives in Edict.Core/EventHandler/; its InvokeHandler executor is
    // internal and bare-named; the new OutboxEffectKind value lives in
    // Edict.Core.Outbox alongside the other three.

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

    // Claim-check contracts: ADR 0024 — universal wire-format envelope,
    // append-only store seam, post-wrap overflow exception, and dead-letter
    // failure-kind discriminator. All live in Edict.Contracts (Orleans-free).

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
    public void AzureBlobClaimCheckStore_ShouldResideInEdictAzureClaimCheck()
    {
        var rule = Classes().That().HaveNameMatching("^AzureBlobClaimCheckStore$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure\.ClaimCheck$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictAzureStreamsOptions_ShouldResideInEdictAzure()
    {
        var rule = Classes().That().HaveNameMatching("^EdictAzureStreamsOptions$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictAzurePersistenceOptions_ShouldResideInEdictAzure()
    {
        var rule = Classes().That().HaveNameMatching("^EdictAzurePersistenceOptions$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure$");

        rule.Check(Architecture);
    }

    [Fact]
    public void AzureTableRepository_ShouldResideInEdictAzure()
    {
        var rule = Classes().That().HaveNameStartingWith("AzureTableRepository")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure\.TableStorage$");

        rule.Check(Architecture);
    }

    [Fact]
    public void AzureTableWriteStoreFactory_ShouldResideInEdictAzure()
    {
        var rule = Classes().That().HaveNameMatching("^AzureTableWriteStoreFactory$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure\.TableStorage$");

        rule.Check(Architecture);
    }
}
