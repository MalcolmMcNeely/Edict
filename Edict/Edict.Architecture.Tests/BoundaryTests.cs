using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;

using Edict.Contracts.Commands;

using Sample.Orders;

using Xunit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using DomainArchitecture = ArchUnitNET.Domain.Architecture;

namespace Edict.Architecture.Tests;

public class BoundaryTests
{
    private static readonly DomainArchitecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(Command).Assembly,
            typeof(PlaceOrderCommand).Assembly)
        .Build();

    [Fact]
    public void EdictContracts_DoesNotDependOnOrleansRuntime()
    {
        var rule = Types().That()
            .ResideInAssembly(typeof(Command).Assembly.GetName().Name!)
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^Orleans")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    [Fact]
    public void SampleOrders_DoesNotDependOnGrainBases()
    {
        var rule = Types().That()
            .ResideInAssembly(typeof(PlaceOrderCommand).Assembly.GetName().Name!)
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^Orleans")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }
}
