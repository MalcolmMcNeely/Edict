using System.Text.RegularExpressions;

using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;

using Edict.Contracts.Commands;
using Edict.Core.Idempotency;
using Edict.Telemetry;

using Sample.Contracts.Orders.Commands;

using ConformanceFixture = Edict.Tests.Conformance.ConformanceFixture;

using Xunit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using DomainArchitecture = ArchUnitNET.Domain.Architecture;

namespace Edict.Architecture.Tests;

public class BoundaryTests
{
    static readonly DomainArchitecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(EdictCommand).Assembly,
            typeof(EdictIdempotencyBase).Assembly,
            typeof(EdictDiagnostics).Assembly,
            typeof(ConformanceFixture).Assembly,
            typeof(PlaceOrderCommand).Assembly)
        .Build();

    [Fact]
    public void EdictContracts_ShouldNotDependOnOrleansRuntime()
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
    public void SampleContracts_ShouldNotDependOnGrainBases()
    {
        var rule = Types().That()
            .ResideInAssembly(typeof(PlaceOrderCommand).Assembly.GetName().Name!)
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^Orleans")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    // Edict.Telemetry may reference Orleans.Core (RequestContext) but must never
    // pull in the Orleans server runtime (grain bases, hosting, etc.).
    [Fact]
    public void EdictTelemetry_ShouldNotDependOnOrleansGrainBase()
    {
        var rule = Types().That()
            .ResideInAssembly(typeof(EdictDiagnostics).Assembly.GetName().Name!)
            .Should()
            .NotDependOnAnyTypesThat()
            .HaveFullNameMatching(@"^Orleans\.Grain$")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    // ITableRepository lives in Edict.Contracts; Azure.Data.Tables is Azure-provider-only.
    [Fact]
    public void EdictContracts_ShouldNotDependOnAzureDataTables()
    {
        var rule = Types().That()
            .ResideInAssembly(typeof(EdictCommand).Assembly.GetName().Name!)
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^Azure\.Data\.Tables")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    // The contracts boundary stays SDK-free: pulling Confluent.Kafka here would
    // force every consumer of EdictCommand/EdictEvent to ship the Kafka client.
    [Fact]
    public void EdictContracts_ShouldNotDependOnConfluentKafka()
    {
        var contractsAssembly = typeof(EdictCommand).Assembly;
        var referenced = contractsAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced,
            a => a.Name is not null
                 && a.Name.StartsWith("Confluent.Kafka", StringComparison.OrdinalIgnoreCase));
    }

    // EdictTableProjectionBuilder is provider-neutral; Azure stays in the
    // write-store implementation, not in the grain base.
    [Fact]
    public void EdictTableProjectionBuilder_ShouldNotDependOnAzure()
    {
        var rule = Classes().That()
            .HaveNameStartingWith("EdictTableProjectionBuilder")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^Azure")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }

    // Conformance owns substrate-agnostic scenarios; pulling any provider SDK
    // (Azure.*, Confluent.Kafka, Npgsql) into it would couple the harness to
    // one substrate and defeat the inheritance pattern.
    [Fact]
    public void EdictTestsConformance_ShouldNotDependOnAnyProviderSdkPackages()
    {
        var conformanceAssembly = typeof(ConformanceFixture).Assembly;
        var referenced = conformanceAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced,
            a => a.Name is not null
                 && (a.Name.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase)
                     || a.Name.StartsWith("Confluent.Kafka", StringComparison.OrdinalIgnoreCase)
                     || a.Name.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)));
    }

    // Azure implementations live in Edict.Azure.Streaming or
    // Edict.Azure.Persistence, not Edict.Core — so taking Core does not drag
    // any Azure SDK into a non-Azure deployment.
    [Fact]
    public void EdictCore_ShouldNotDependOnAnyAzureSdkPackages()
    {
        var coreAssembly = typeof(EdictIdempotencyBase).Assembly;
        var referenced = coreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced,
            a => a.Name is not null
                 && a.Name.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase));
    }

    // ADR-0042 drift guard: the only framework assemblies allowed to reference
    // an Azure.* SDK are Edict.Azure.Streaming and Edict.Azure.Persistence. A
    // future regression that re-bundles AQS + Tables into one assembly, or
    // leaks an Azure dependency into Edict.Core / Edict.Kafka / Edict.Postgres,
    // shows up here as an unexpected carrier.
    [Fact]
    public void EdictAzureSdks_ShouldOnlyResideInTheTwoAzurePackages()
    {
        var permitted = new HashSet<string>(StringComparer.Ordinal)
        {
            "Edict.Azure.Streaming",
            "Edict.Azure.Persistence",
        };

        var frameworkAssemblies = new[]
        {
            typeof(EdictCommand).Assembly,
            typeof(EdictIdempotencyBase).Assembly,
            typeof(EdictDiagnostics).Assembly,
            typeof(Edict.Azure.Streaming.EdictAzureStreamsOptions).Assembly,
            typeof(Edict.Azure.Persistence.EdictAzurePersistenceOptions).Assembly,
            typeof(Edict.Kafka.EdictKafkaStreamsOptions).Assembly,
            typeof(Edict.Postgres.EdictPostgresPersistenceOptions).Assembly,
        };

        var violations = frameworkAssemblies
            .Where(assembly => !permitted.Contains(assembly.GetName().Name!))
            .SelectMany(assembly => assembly.GetReferencedAssemblies()
                .Where(referenced => referenced.Name is not null
                    && referenced.Name.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase))
                .Select(referenced => $"{assembly.GetName().Name} → {referenced.Name}"))
            .ToList();

        Assert.Empty(violations);
    }

    // Kafka implementations live in Edict.Kafka, not Edict.Core — taking Core
    // must not drag Confluent.Kafka into a non-Kafka deployment.
    [Fact]
    public void EdictCore_ShouldNotDependOnConfluentKafka()
    {
        var coreAssembly = typeof(EdictIdempotencyBase).Assembly;
        var referenced = coreAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced,
            a => a.Name is not null
                 && a.Name.StartsWith("Confluent.Kafka", StringComparison.OrdinalIgnoreCase));
    }

    // every public, non-nested type in Edict.Contracts is Edict-prefixed.
    // The prefix signals "consumer contract with the framework"; unprefixed public types
    // would dilute that signal and risk naming collisions.
    [Fact]
    public void EdictContracts_ShouldHaveAllPublicTypesBrandPrefixed()
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
    public void Solution_ShouldContainNoGitkeepFiles()
    {
        var solutionRoot = GetSolutionRoot();
        var gitkeepFiles = Directory
            .EnumerateFiles(solutionRoot, ".gitkeep", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
            .ToList();

        Assert.Empty(gitkeepFiles);
    }

    static string GetSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    // every tunable knob lives on an options class with its default
    // in the constructor; literals in mechanism code are forbidden. This guard
    // catches a regression like a future maintainer slipping a
    // TimeSpan.FromMinutes(1) into the engine instead of surfacing it through
    // EdictOptions. Options classes (filenames matching *Options.cs) are the
    // sanctioned home for literals — the test scans every other source file
    // in Edict.Core, Edict.Azure.Streaming, and Edict.Azure.Persistence.
    [Fact]
    public void EdictMechanismCode_ShouldNotContainTimeSpanLiteralDefaults()
    {
        var solutionRoot = GetSolutionRoot();
        var mechanismRoots = new[]
        {
            Path.Combine(solutionRoot, "Edict.Core"),
            Path.Combine(solutionRoot, "Edict.Azure.Streaming"),
            Path.Combine(solutionRoot, "Edict.Azure.Persistence"),
        };

        var literalPattern = new Regex(
            @"TimeSpan\.From(Minutes|Seconds|Milliseconds|Hours)\s*\(\s*\d",
            RegexOptions.Compiled);

        var violations = new List<string>();
        foreach (var root in mechanismRoots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                {
                    continue;
                }
                if (Path.GetFileName(file).EndsWith("Options.cs", StringComparison.Ordinal))
                {
                    continue;
                }

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (literalPattern.IsMatch(lines[i]))
                    {
                        violations.Add($"{file}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        Assert.Empty(violations);
    }
}
