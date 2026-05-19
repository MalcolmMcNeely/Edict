using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;

using Edict.Azure.TableStorage;
using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.TableStorage;
using Edict.Core.Idempotency;
using Edict.Core.Projections;

using Sample.Orders;

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
            typeof(EdictEventIdempotentGrain).Assembly,
            typeof(PlaceOrderCommand).Assembly)
        .Build();

    // Contracts: Event, [Stream], [RouteKey], ITableRepository — ADR 0008 / ADR 0012

    [Fact]
    public void Event_ResidiesInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^EdictEvent$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Events$");

        rule.Check(Architecture);
    }

    [Fact]
    public void StreamAttribute_ResidiesInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^EdictStreamAttribute$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Events$");

        rule.Check(Architecture);
    }

    [Fact]
    public void RouteKeyAttribute_ResidiesInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^EdictRouteKeyAttribute$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Commands$");

        rule.Check(Architecture);
    }

    [Fact]
    public void ITableRepository_ResidiesInEdictContracts()
    {
        var rule = Interfaces().That().HaveNameStartingWith("IEdictTableRepository")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.TableStorage$");

        rule.Check(Architecture);
    }

    [Fact]
    public void ITableWriteStore_ResidiesInEdictContracts()
    {
        var rule = Interfaces().That().HaveNameStartingWith("IEdictTableWriteStore")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.TableStorage$");

        rule.Check(Architecture);
    }

    // Core runtime: EdictEventDeduplicationGrain, EdictProjectionBuilderGrain, EdictTableProjectionBuilderGrain — ADR 0008

    [Fact]
    public void EventDeduplicationGrain_ResidiesInEdictCore()
    {
        var rule = Classes().That().HaveNameMatching("^EdictEventDeduplicationGrain$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Dedup$");

        rule.Check(Architecture);
    }

    [Fact]
    public void ProjectionBuilderGrain_ResidiesInEdictCore()
    {
        var rule = Classes().That().HaveNameMatching("^EdictProjectionBuilderGrain$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Projections$");

        rule.Check(Architecture);
    }

    [Fact]
    public void TableProjectionBuilderGrain_ResidiesInEdictCore()
    {
        var rule = Classes().That().HaveNameStartingWith("EdictTableProjectionBuilderGrain")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Projections$");

        rule.Check(Architecture);
    }

    // Azure provider: AzureTableRepository, AzureTableWriteStoreFactory — ADR 0014

    [Fact]
    public void AzureTableRepository_ResidiesInEdictAzure()
    {
        var rule = Classes().That().HaveNameStartingWith("AzureTableRepository")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure\.TableStorage$");

        rule.Check(Architecture);
    }

    [Fact]
    public void AzureTableWriteStoreFactory_ResidiesInEdictAzure()
    {
        var rule = Classes().That().HaveNameMatching("^AzureTableWriteStoreFactory$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Azure\.TableStorage$");

        rule.Check(Architecture);
    }
}
