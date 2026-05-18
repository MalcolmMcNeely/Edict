using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.TableStorage;
using Edict.Core.Grains;
using Edict.Core.TableStorage;

using Sample.Orders;

using Xunit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using DomainArchitecture = ArchUnitNET.Domain.Architecture;

namespace Edict.Architecture.Tests;

public class TypePlacementTests
{
    private static readonly DomainArchitecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(Command).Assembly,
            typeof(EventDeduplicationGrain).Assembly,
            typeof(PlaceOrderCommand).Assembly)
        .Build();

    // Contracts: Event, [Stream], [RouteKey], ITableRepository — ADR 0008 / ADR 0012

    [Fact]
    public void Event_ResidiesInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^Event$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Events$");

        rule.Check(Architecture);
    }

    [Fact]
    public void StreamAttribute_ResidiesInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^StreamAttribute$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Events$");

        rule.Check(Architecture);
    }

    [Fact]
    public void RouteKeyAttribute_ResidiesInEdictContracts()
    {
        var rule = Types().That().HaveNameMatching("^RouteKeyAttribute$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.Commands$");

        rule.Check(Architecture);
    }

    [Fact]
    public void ITableRepository_ResidiesInEdictContracts()
    {
        var rule = Interfaces().That().HaveNameStartingWith("ITableRepository")
            .Should().ResideInNamespaceMatching(@"^Edict\.Contracts\.TableStorage$");

        rule.Check(Architecture);
    }

    // Core runtime: EventDeduplicationGrain, ProjectionBuilderGrain, TableProjectionBuilderGrain, AzureTableRepository — ADR 0008

    [Fact]
    public void EventDeduplicationGrain_ResidiesInEdictCore()
    {
        var rule = Classes().That().HaveNameMatching("^EventDeduplicationGrain$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Grains$");

        rule.Check(Architecture);
    }

    [Fact]
    public void ProjectionBuilderGrain_ResidiesInEdictCore()
    {
        var rule = Classes().That().HaveNameMatching("^ProjectionBuilderGrain$")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Grains$");

        rule.Check(Architecture);
    }

    [Fact]
    public void TableProjectionBuilderGrain_ResidiesInEdictCore()
    {
        var rule = Classes().That().HaveNameStartingWith("TableProjectionBuilderGrain")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.Grains$");

        rule.Check(Architecture);
    }

    [Fact]
    public void AzureTableRepository_ResidiesInEdictCore()
    {
        var rule = Classes().That().HaveNameStartingWith("AzureTableRepository")
            .Should().ResideInNamespaceMatching(@"^Edict\.Core\.TableStorage$");

        rule.Check(Architecture);
    }
}
