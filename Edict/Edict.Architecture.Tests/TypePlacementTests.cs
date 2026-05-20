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

    [Fact]
    public void DeadLetterEntry_ShouldResideInEdictCoreDeadLetter()
    {
        var rule = Types().That().HaveNameMatching("^DeadLetterEntry$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.DeadLetter$");

        rule.Check(Architecture);
    }

    // ADR 0019 consumer surface: the read-only repository + its DTO are
    // consumer-facing, so they are brand-prefixed and live in Edict.Contracts
    // (mirroring IEdictTableRepository); redrive is the only mutation path and
    // is a grain method, so IEdictDeadLetterAdmin is a grain interface in
    // Edict.Core (it cannot sit in the bare-named Edict.Core.DeadLetter ns).

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
    public void IEdictDeadLetterAdmin_ShouldResideInEdictCoreAdministration()
    {
        var rule = Interfaces().That().HaveNameMatching("^IEdictDeadLetterAdmin$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Administration$");

        rule.Check(Architecture);
    }

    [Fact]
    public void EdictOutboxSaturatedException_ShouldResideInEdictCoreRoot()
    {
        var rule = Classes().That().HaveNameMatching("^EdictOutboxSaturatedException$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core$");

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
        // EdictDurableConsumerBase is the deliberate exception: ADR 0017
        // clause (b) names it as the new outer shared-inheritance root for
        // the two consumer-facing grain bases, so it is brand-prefixed even
        // though it lives in the Outbox folder alongside the engine seam.
        var rule = Types().That()
            .ResideInNamespaceMatching(@"^Edict\.Core\.(Outbox|DeadLetter)$")
            .And().DoNotHaveNameStartingWith("EdictDurableConsumerBase")
            .Should().HaveNameMatching("^(?!Edict)")
            .AndShould().HaveNameMatching("^(?!IEdict)");

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
