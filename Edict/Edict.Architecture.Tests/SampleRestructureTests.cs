using Sample.Domain.Orders.CommandHandlers;
using Sample.Web.Components.Simulator;

using Xunit;

namespace Edict.Architecture.Tests;

// The Sample restructure (issue #137) extracts substrate-agnostic Sample.Silo
// code into a Sample.Domain class library and substrate-agnostic Sample.Web
// code into a Sample.Web.Components Razor class library, so future Kafka/Postgres
// samples can reuse both. The assertions here lock in that extraction:
// handlers/sagas/projection builders/state must ship from Sample.Domain;
// pages/layouts/simulator/state must ship from Sample.Web.Components. The
// Sample.* host projects (Sample.Silo, Sample.Web, Sample.KafkaPostgres.Silo,
// Sample.KafkaPostgres.Web) keep only their substrate-specific Program.cs
// (issue #140 — the Kafka+Postgres sibling sample reuses Sample.Domain +
// Sample.Web.Components against Edict.Kafka + Edict.Postgres).
public class SampleRestructureTests
{
    [Fact]
    public void OrderCommandHandler_ShouldResideInSampleDomain()
    {
        Assert.Equal("Sample.Domain", typeof(OrderCommandHandler).Assembly.GetName().Name);
    }

    [Fact]
    public void IDeterministicOrderPlacer_ShouldResideInSampleWebComponents()
    {
        Assert.Equal("Sample.Web.Components", typeof(IDeterministicOrderPlacer).Assembly.GetName().Name);
    }

    [Theory]
    [InlineData("Sample.Silo")]
    [InlineData("Sample.Web")]
    [InlineData("Sample.KafkaPostgres.Silo")]
    [InlineData("Sample.KafkaPostgres.Web")]
    public void SampleHostProject_ShouldContainOnlyProgramCs(string projectName)
    {
        // Host projects are substrate-specific entry points. Any other .cs file
        // creeping in is a sign that consumer code (handlers, projections, web
        // components) has leaked back out of Sample.Domain / Sample.Web.Components
        // and broken the substrate-agnostic-reuse story.
        var projectDir = Path.Combine(SolutionRoot, "Sample", projectName);
        Assert.True(Directory.Exists(projectDir), $"{projectName} project directory missing");

        var sourceFiles = Directory
            .EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["Program.cs"], sourceFiles);
    }

    [Theory]
    [InlineData("Sample.KafkaPostgres.Silo", "Sample.Domain")]
    [InlineData("Sample.KafkaPostgres.Silo", "Edict.Kafka")]
    [InlineData("Sample.KafkaPostgres.Silo", "Edict.Postgres")]
    [InlineData("Sample.KafkaPostgres.Web", "Sample.Web.Components")]
    [InlineData("Sample.KafkaPostgres.Web", "Edict.Postgres")]
    public void SampleKafkaPostgresHost_ShouldReferenceProject(string projectName, string referencedProject)
    {
        // The KafkaPostgres host projects must consume the substrate-agnostic
        // domain/UI libraries (proving the #137 extraction holds) and bind to
        // the Kafka/Postgres provider packages (not Edict.Azure). The csproj
        // string-match is the cheapest pin against a regression that copies
        // Azure references into the new hosts.
        var csprojPath = Path.Combine(SolutionRoot, "Sample", projectName, $"{projectName}.csproj");
        Assert.True(File.Exists(csprojPath), $"{projectName}.csproj missing");

        var content = File.ReadAllText(csprojPath);
        Assert.Contains($"{referencedProject}.csproj", content);
    }

    [Fact]
    public void SampleKafkaPostgresAppHost_ShouldReferenceBothHosts()
    {
        // Aspire AppHost orchestrates Silo + Web for the Kafka+Postgres pairing.
        // The Azure AppHost has the same shape against Sample.Silo + Sample.Web;
        // pinning both references here keeps the two sibling AppHosts symmetric.
        var csprojPath = Path.Combine(SolutionRoot, "Sample", "Sample.KafkaPostgres.AppHost",
            "Sample.KafkaPostgres.AppHost.csproj");
        Assert.True(File.Exists(csprojPath), "Sample.KafkaPostgres.AppHost.csproj missing");

        var content = File.ReadAllText(csprojPath);
        Assert.Contains("Sample.KafkaPostgres.Silo.csproj", content);
        Assert.Contains("Sample.KafkaPostgres.Web.csproj", content);
    }

    static string SolutionRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            {
                dir = dir.Parent;
            }
            return dir?.Parent?.FullName ?? AppContext.BaseDirectory;
        }
    }
}
