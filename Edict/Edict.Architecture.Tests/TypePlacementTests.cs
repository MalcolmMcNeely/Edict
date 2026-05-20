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
    public void EdictOutboxOptions_ShouldResideInEdictContractsConfiguration()
    {
        var rule = Types().That().HaveNameMatching("^EdictOutboxOptions$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Configuration$");

        rule.Check(Architecture);
    }

    [Fact]
    public void OutboxAndDeadLetterEngine_ShouldBeBareNamed_NoConsumerTypesIt()
    {
        // Two deliberate brand-prefixed exceptions in these folders:
        // - EdictDurableConsumerBase: ADR 0017 clause (b) names it as the outer
        //   shared-inheritance root for the consumer-facing grain bases.
        // - EdictDeadLetterProjectionBuilder: the framework-shipped projection
        //   grain whose role-named subclass naturally inherits the brand from
        //   EdictTableProjectionBuilder (ADR 0022 — auto-wired by AddEdict()).
        var rule = Types().That()
            .ResideInNamespaceMatching(@"^Edict\.Core\.(Outbox|DeadLetter)$")
            .And().DoNotHaveNameStartingWith("EdictDurableConsumerBase")
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

    [Fact]
    public void EdictDurableConsumerBase_ShouldResideInEdictCoreOutbox()
    {
        var rule = Classes().That().HaveNameStartingWith("EdictDurableConsumerBase")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Outbox$");

        rule.Check(Architecture);
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
