using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;

using Edict.Contracts.Commands;
using Edict.Core.Idempotency;
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
            typeof(EdictEventIdempotentGrain).Assembly,
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

    // ADR 0012: ITableRepository lives in Edict.Contracts; Azure.Data.Tables is Azure-provider-only.
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

    // ADR 0014: Azure implementations live in Edict.Azure, not Edict.Core — so taking
    // Core does not drag any Azure SDK into a non-Azure deployment.
    [Fact]
    public void EdictCore_DoesNotDependOnAnyAzureSdkPackages()
    {
        var coreAssembly = typeof(EdictEventIdempotentGrain).Assembly;
        var referenced = coreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced,
            a => a.Name is not null
                 && a.Name.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase));
    }

    // ADR 0013: every public, non-nested type in Edict.Contracts is Edict-prefixed.
    // The prefix signals "consumer contract with the framework"; unprefixed public types
    // would dilute that signal and risk naming collisions.
    [Fact]
    public void All_public_Edict_Contracts_types_are_brand_prefixed()
    {
        var contractsAssembly = typeof(EdictCommand).Assembly;
        var violations = contractsAssembly
            .GetExportedTypes()
            .Where(t => !t.IsNested)
            .Where(t => !t.Name.StartsWith("Edict", StringComparison.Ordinal)
                     && !t.Name.StartsWith("IEdict", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToList();

        Assert.Empty(violations);
    }

    // No stray .gitkeep placeholder files left in the solution tree.
    // A .gitkeep indicates an empty directory placeholder; when real content arrives
    // the file must be deleted. Leaving it in causes confusion and bloats the index.
    [Fact]
    public void No_gitkeep_files_exist_in_solution()
    {
        var solutionRoot = GetSolutionRoot();
        var gitkeepFiles = Directory
            .EnumerateFiles(solutionRoot, ".gitkeep", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
            .ToList();

        Assert.Empty(gitkeepFiles);
    }

    private static string GetSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
