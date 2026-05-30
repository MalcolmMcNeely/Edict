using Edict.Mcp.Handlers;
using Edict.Mcp.SiloWiring;
using Edict.Mcp.Tools;
using Edict.Mcp.Tests.Versioning;

using static VerifyXunit.Verifier;

namespace Edict.Mcp.Tests.Tools;

public class DescribeSiloWiringToolTests
{
    [Fact]
    public async Task InvokeAsync_RendersWiredAndMissingExtensionsAsStructuredJson()
    {
        // Arrange
        var report = new SiloWiringReport(
            ProgramSourceLocation: new SourceLocationInfo("ConsumerHost/Program.cs", 1, 1),
            Wired:
            [
                new SiloWiringEntry(
                    ExtensionName: "AddEdict",
                    DeclaringAssembly: "Edict.Core",
                    Purpose: "Registers the Edict framework: handler discovery, outbox, telemetry."),
                new SiloWiringEntry(
                    ExtensionName: "AddEdictAzureStreams",
                    DeclaringAssembly: "Edict.Azure.Streaming",
                    Purpose: "Wires the Azure Queue stream provider."),
            ],
            Missing:
            [
                new SiloWiringEntry(
                    ExtensionName: "AddEdictAzureBlobClaimCheck",
                    DeclaringAssembly: "Edict.Azure.Streaming",
                    Purpose: "Enables the Azure Blob claim-check store for large event payloads."),
            ]);
        var tool = new DescribeSiloWiringTool(_ => Task.FromResult(report), StubVersionReportProvider.Clean());

        // Act
        var responseJson = await tool.InvokeAsync(arguments: null, CancellationToken.None);

        // Assert
        await Verify(responseJson);
    }
}
