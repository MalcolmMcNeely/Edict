using Edict.Mcp.Configuration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Edict.Mcp.Tests.Configuration;

public class ConfigurationCheckScannerTests
{
    [Fact]
    public void Scan_KafkaStreamsWiredWithoutBootstrapServers_EmitsRequiredMissingFinding()
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
                            .AddEdictKafkaStreams(options => { options.ConsumerGroupId = "orders"; });
                    }
                }
            }
            """;
        var compilation = CreateCompilationWithProgramCs(programSource);
        var scanner = new ConfigurationCheckScanner();

        // Act
        var report = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var finding = Assert.Single(report.Findings);
        Assert.Equal(ConfigurationFindingSeverity.Error, finding.Severity);
        Assert.Equal(ConfigurationFindingCategory.RequiredMissing, finding.Category);
        Assert.Equal("EdictKafkaStreamsOptions", finding.OptionsType);
        Assert.Equal("BootstrapServers", finding.Knob);
    }

    [Fact]
    public void Scan_PostgresPersistenceWiredWithoutConnectionString_EmitsRequiredMissingFinding()
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
                            .AddEdictPostgresPersistence(options => { });
                    }
                }
            }
            """;
        var compilation = CreateCompilationWithProgramCs(programSource);
        var scanner = new ConfigurationCheckScanner();

        // Act
        var report = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var finding = Assert.Single(report.Findings);
        Assert.Equal(ConfigurationFindingSeverity.Error, finding.Severity);
        Assert.Equal(ConfigurationFindingCategory.RequiredMissing, finding.Category);
        Assert.Equal("EdictPostgresPersistenceOptions", finding.OptionsType);
        Assert.Equal("ConnectionString", finding.Knob);
    }

    [Fact]
    public void Scan_AzureStreamsWired_EmitsConfirmClientReminderAsInfo()
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
                            .AddEdictAzureStreams(options => { });
                    }
                }
            }
            """;
        var compilation = CreateCompilationWithProgramCs(programSource);
        var scanner = new ConfigurationCheckScanner();

        // Act
        var report = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        var finding = Assert.Single(report.Findings);
        Assert.Equal(ConfigurationFindingSeverity.Info, finding.Severity);
        Assert.Equal(ConfigurationFindingCategory.RequiredMissing, finding.Category);
        Assert.Equal("EdictAzureStreamsOptions", finding.OptionsType);
        Assert.Equal("QueueServiceClient", finding.Knob);
    }

    [Fact]
    public void Scan_QueueServiceClientSetOnOptions_DoesNotEmitConfirmClientReminder()
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
                            .AddEdictAzureStreams(options => { options.QueueServiceClient = new object(); });
                    }
                }
            }
            """;
        var compilation = CreateCompilationWithProgramCs(programSource);
        var scanner = new ConfigurationCheckScanner();

        // Act
        var report = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Scan_BootstrapServersSetFromLocalVariable_RegistersAsSet()
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
                        var brokers = "localhost:9092";
                        siloBuilder
                            .AddEdict()
                            .AddEdictKafkaStreams(options => { options.BootstrapServers = brokers; });
                    }
                }
            }
            """;
        var compilation = CreateCompilationWithProgramCs(programSource);
        var scanner = new ConfigurationCheckScanner();

        // Act
        var report = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Scan_ConnectionStringSetFromConfiguration_RegistersAsSet()
    {
        // Arrange
        const string programSource = """
            using Edict.Hosting;
            using Orleans.Hosting;

            namespace ConsumerHost
            {
                public static class Program
                {
                    public static void Configure(ISiloBuilder siloBuilder, IAppConfiguration configuration)
                    {
                        siloBuilder
                            .AddEdict()
                            .AddEdictPostgresPersistence(options => { options.ConnectionString = configuration["Postgres"]; });
                    }
                }
            }
            """;
        var compilation = CreateCompilationWithProgramCs(programSource);
        var scanner = new ConfigurationCheckScanner();

        // Act
        var report = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Scan_NoProgramCsAnywhereInSolution_ReturnsEmptyReport()
    {
        // Arrange
        var compilation = CreateCompilationFromTrees(
            "ConsumerLibrary",
            CSharpSyntaxTree.ParseText(EdictWiringStubsSource, path: "Stubs.cs"));
        var scanner = new ConfigurationCheckScanner();

        // Act
        var report = scanner.Scan([compilation], solutionDirectory: null);

        // Assert
        Assert.Null(report.ProgramSourceLocation);
        Assert.Empty(report.Findings);
    }

    static CSharpCompilation CreateCompilationWithProgramCs(string programSource)
    {
        var stubsTree = CSharpSyntaxTree.ParseText(EdictWiringStubsSource, path: "EdictStubs.cs");
        var programTree = CSharpSyntaxTree.ParseText(programSource, path: "Program.cs");
        return CreateCompilationFromTrees("ConsumerHost", stubsTree, programTree);
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

    const string EdictWiringStubsSource = """
        namespace Orleans.Hosting
        {
            public interface ISiloBuilder { }
        }

        namespace ConsumerHost
        {
            public interface IAppConfiguration
            {
                string this[string key] { get; }
            }
        }

        namespace Edict.Configuration
        {
            public sealed class EdictKafkaStreamsOptions
            {
                public string BootstrapServers { get; set; } = "";
                public string ConsumerGroupId { get; set; } = "edict-silo";
            }

            public sealed class EdictPostgresPersistenceOptions
            {
                public string ConnectionString { get; set; } = "";
            }

            public sealed class EdictAzureStreamsOptions
            {
                public object? QueueServiceClient { get; set; }
            }
        }

        namespace Edict.Hosting
        {
            using System;

            using Orleans.Hosting;

            using Edict.Configuration;

            public static class EdictSiloBuilderExtensions
            {
                public static ISiloBuilder AddEdict(this ISiloBuilder siloBuilder) => siloBuilder;
            }

            public static class EdictKafkaSiloBuilderExtensions
            {
                public static ISiloBuilder AddEdictKafkaStreams(this ISiloBuilder siloBuilder, Action<EdictKafkaStreamsOptions> configure) => siloBuilder;
            }

            public static class EdictPostgresSiloBuilderExtensions
            {
                public static ISiloBuilder AddEdictPostgresPersistence(this ISiloBuilder siloBuilder, Action<EdictPostgresPersistenceOptions> configure) => siloBuilder;
            }

            public static class EdictAzureStreamingSiloBuilderExtensions
            {
                public static ISiloBuilder AddEdictAzureStreams(this ISiloBuilder siloBuilder, Action<EdictAzureStreamsOptions> configure) => siloBuilder;
            }
        }
        """;
}
