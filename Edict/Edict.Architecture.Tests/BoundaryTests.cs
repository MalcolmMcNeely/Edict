using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;

using Edict.Contracts.Commands;
using Edict.Core.Grains;
using Edict.Telemetry;

using Sample.Orders;

using Xunit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using DomainArchitecture = ArchUnitNET.Domain.Architecture;

namespace Edict.Architecture.Tests;

public class BoundaryTests
{
    private static readonly DomainArchitecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(EdictCommand).Assembly,
            typeof(EdictEventDeduplicationGrain).Assembly,
            typeof(EdictDiagnostics).Assembly,
            typeof(PlaceOrderCommand).Assembly)
        .Build();

    [Fact]
    public void EdictContracts_DoesNotDependOnOrleansRuntime()
    {
        var rule = Types().That()
            .ResideInAssembly(typeof(EdictCommand).Assembly.GetName().Name!)
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

    // ADR 0014: Edict.Telemetry may reference Orleans.Core (RequestContext) but must never
    // pull in the Orleans server runtime (grain bases, hosting, etc.).
    [Fact]
    public void EdictTelemetry_DoesNotDependOnOrleansGrainBase()
    {
        var rule = Types().That()
            .ResideInAssembly(typeof(EdictDiagnostics).Assembly.GetName().Name!)
            .Should()
            .NotDependOnAnyTypesThat()
            .HaveFullNameMatching(@"^Orleans\.Grain$")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    // ADR 0012: ITableRepository lives in Edict.Contracts; Azure.Data.Tables is Core-only.
    [Fact]
    public void EdictContracts_DoesNotDependOnAzureDataTables()
    {
        var rule = Types().That()
            .ResideInAssembly(typeof(EdictCommand).Assembly.GetName().Name!)
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^Azure\.Data\.Tables")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    // ADR 0015: EdictTableProjectionBuilderGrain is provider-neutral; Azure stays in the
    // write-store implementation, not in the grain base.
    [Fact]
    public void TableProjectionBuilderGrain_DoesNotDependOnAzure()
    {
        var rule = Classes().That()
            .HaveNameStartingWith("EdictTableProjectionBuilderGrain")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^Azure")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }
}
