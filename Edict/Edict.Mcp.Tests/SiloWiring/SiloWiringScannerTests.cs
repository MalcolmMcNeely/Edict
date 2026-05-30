using Edict.Mcp.SiloWiring;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Edict.Mcp.Tests.SiloWiring;

public class SiloWiringScannerTests
{
    [Fact]
    public void Scan_ProgramCsWithSingleAddEdictCall_ReportsExtensionAsWired()
    {
        // Arrange
        const string programSource = """
            using Edict.Hosting;
            using Orleans.Hosting;

            namespace ConsumerHost
            {
                public static class Program
                {
                    public static void Configure(ISiloBuilder siloBuilder)
                    {
                        siloBuilder.AddEdict();
                    }
                }
            }
            """;
        var compilation = CreateCompilationWithProgramCs(programSource);
        var scanner = new SiloWiringScanner();

        // Act
        var report = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var wired = Assert.Single(report.Wired);
        Assert.Equal("AddEdict", wired.ExtensionName);
    }

    [Fact]
    public void Scan_ClaimCheckMissing_SurfacesAddEdictAzureBlobClaimCheckInMissingList()
    {
        // Arrange
        const string programSource = """
            using Edict.Hosting;
            using Orleans.Hosting;

            namespace ConsumerHost
            {
                public static class Program
                {
                    public static void Configure(ISiloBuilder siloBuilder)
                    {
                        siloBuilder
                            .AddEdict()
                            .AddEdictAzurePersistence()
                            .AddEdictAzureStreams();
                    }
                }
            }
            """;
        var compilation = CreateCompilationWithProgramCs(programSource);
        var scanner = new SiloWiringScanner();

        // Act
        var report = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        Assert.Contains(report.Missing, entry => entry.ExtensionName == "AddEdictAzureBlobClaimCheck");
        Assert.DoesNotContain(report.Missing, entry => entry.ExtensionName == "AddEdict");
        Assert.DoesNotContain(report.Missing, entry => entry.ExtensionName == "AddEdictAzureStreams");
    }

    [Fact]
    public void Scan_NoProgramCsAnywhereInSolution_ReturnsEmptyReportWithoutMissingCatalogue()
    {
        // Arrange
        var compilation = CreateCompilationFromTrees(
            "ConsumerLibrary",
            CSharpSyntaxTree.ParseText(SiloBuilderStubsSource, path: "Stubs.cs"));
        var scanner = new SiloWiringScanner();

        // Act
        var report = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        Assert.Null(report.ProgramSourceLocation);
        Assert.Empty(report.Wired);
        Assert.Empty(report.Missing);
    }

    [Fact]
    public void Scan_ProgramCsChainsMultipleAddEdictExtensions_ReportsAllWiredInChainOrder()
    {
        // Arrange
        const string programSource = """
            using Edict.Hosting;
            using Orleans.Hosting;

            namespace ConsumerHost
            {
                public static class Program
                {
                    public static void Configure(ISiloBuilder siloBuilder)
                    {
                        siloBuilder
                            .AddEdict()
                            .AddEdictAzurePersistence()
                            .AddEdictAzureStreams()
                            .AddEdictAzureBlobClaimCheck();
                    }
                }
            }
            """;
        var compilation = CreateCompilationWithProgramCs(programSource);
        var scanner = new SiloWiringScanner();

        // Act
        var report = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var wiredNames = report.Wired.Select(entry => entry.ExtensionName).ToList();
        Assert.Equal(
            ["AddEdict", "AddEdictAzurePersistence", "AddEdictAzureStreams", "AddEdictAzureBlobClaimCheck"],
            wiredNames);
    }

    static CSharpCompilation CreateCompilationWithProgramCs(string programSource)
    {
        var basesTree = CSharpSyntaxTree.ParseText(SiloBuilderStubsSource, path: "EdictStubs.cs");
        var programTree = CSharpSyntaxTree.ParseText(programSource, path: "Program.cs");
        return CreateCompilationFromTrees("ConsumerHost", basesTree, programTree);
    }

    static CSharpCompilation CreateCompilationFromTrees(string assemblyName, params SyntaxTree[] syntaxTrees)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Cast<MetadataReference>()
            .ToList();
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    const string SiloBuilderStubsSource = """
        namespace Orleans.Hosting
        {
            public interface ISiloBuilder { }
        }

        namespace Edict.Hosting
        {
            using Orleans.Hosting;

            public static class EdictSiloBuilderExtensions
            {
                public static ISiloBuilder AddEdict(this ISiloBuilder siloBuilder) => siloBuilder;
            }

            public static class EdictAzureStreamingSiloBuilderExtensions
            {
                public static ISiloBuilder AddEdictAzureStreams(this ISiloBuilder siloBuilder) => siloBuilder;
                public static ISiloBuilder AddEdictAzureBlobClaimCheck(this ISiloBuilder siloBuilder) => siloBuilder;
            }

            public static class EdictAzurePersistenceSiloBuilderExtensions
            {
                public static ISiloBuilder AddEdictAzurePersistence(this ISiloBuilder siloBuilder) => siloBuilder;
            }

            public static class EdictPostgresSiloBuilderExtensions
            {
                public static ISiloBuilder AddEdictPostgresPersistence(this ISiloBuilder siloBuilder) => siloBuilder;
            }

            public static class EdictKafkaSiloBuilderExtensions
            {
                public static ISiloBuilder AddEdictKafkaStreams(this ISiloBuilder siloBuilder) => siloBuilder;
            }
        }
        """;
}
