using Edict.Mcp.Handlers;
using Edict.Mcp.Tools;
using Edict.Mcp.Versioning;
using Edict.Mcp.Workspaces;
using Edict.Mcp.Tests.Versioning;

using static VerifyXunit.Verifier;

namespace Edict.Mcp.Tests.Tools;

public class DescribeMcpStateToolTests
{
    [Fact]
    public async Task InvokeAsync_CurrentSkillBodies_ReportsLoadedSolutionPath_HandlerCount_VersionReport_SkillBodies_AndRegisteredTools()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        temporaryDirectory.WriteFile("Workspace.slnx", "<Solution />");
        var workspaceProvider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => temporaryDirectory.Path);
        var inventory = BuildStubInventory();
        var tools = BuildStubTools();
        var versionReport = BuildCleanVersionReport();
        var skillBodiesReport = new SkillBodiesReport(
            ManifestPath: SkillsManifest.ManifestPath,
            InstalledVersion: "0.3.0",
            ToolVersion: "0.3.0",
            DriftStatus: "current");
        var describeMcpState = new DescribeMcpStateTool(
            workspaceProvider,
            _ => Task.FromResult(inventory),
            StubVersionReportProvider.ForReport(versionReport),
            () => skillBodiesReport,
            () => tools);

        // Act
        var responseJson = await describeMcpState.InvokeAsync(null, CancellationToken.None);

        // Assert
        await Verify(responseJson)
            .ScrubLinesWithReplace(line => line.TrimStart().StartsWith("\"loadedSolutionPath\":")
                ? "  \"loadedSolutionPath\": \"{TEMP_SOLUTION_PATH}\","
                : line);
    }

    [Fact]
    public async Task InvokeAsync_StaleSkillBodies_RendersStaleBlockWithRemediation()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        temporaryDirectory.WriteFile("Workspace.slnx", "<Solution />");
        var workspaceProvider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => temporaryDirectory.Path);
        var inventory = BuildStubInventory();
        var tools = BuildStubTools();
        var versionReport = BuildCleanVersionReport();
        var skillBodiesReport = new SkillBodiesReport(
            ManifestPath: SkillsManifest.ManifestPath,
            InstalledVersion: "0.2.1",
            ToolVersion: "0.3.0",
            DriftStatus: "stale");
        var describeMcpState = new DescribeMcpStateTool(
            workspaceProvider,
            _ => Task.FromResult(inventory),
            StubVersionReportProvider.ForReport(versionReport),
            () => skillBodiesReport,
            () => tools);

        // Act
        var responseJson = await describeMcpState.InvokeAsync(null, CancellationToken.None);

        // Assert
        await Verify(responseJson)
            .ScrubLinesWithReplace(line => line.TrimStart().StartsWith("\"loadedSolutionPath\":")
                ? "  \"loadedSolutionPath\": \"{TEMP_SOLUTION_PATH}\","
                : line);
    }

    [Fact]
    public async Task InvokeAsync_MissingSkillBodies_OmitsInstalledVersion_AndRendersBootstrapRemediation()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        temporaryDirectory.WriteFile("Workspace.slnx", "<Solution />");
        var workspaceProvider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => temporaryDirectory.Path);
        var inventory = BuildStubInventory();
        var tools = BuildStubTools();
        var versionReport = BuildCleanVersionReport();
        var skillBodiesReport = new SkillBodiesReport(
            ManifestPath: SkillsManifest.ManifestPath,
            InstalledVersion: null,
            ToolVersion: "0.3.0",
            DriftStatus: "missing");
        var describeMcpState = new DescribeMcpStateTool(
            workspaceProvider,
            _ => Task.FromResult(inventory),
            StubVersionReportProvider.ForReport(versionReport),
            () => skillBodiesReport,
            () => tools);

        // Act
        var responseJson = await describeMcpState.InvokeAsync(null, CancellationToken.None);

        // Assert
        await Verify(responseJson)
            .ScrubLinesWithReplace(line => line.TrimStart().StartsWith("\"loadedSolutionPath\":")
                ? "  \"loadedSolutionPath\": \"{TEMP_SOLUTION_PATH}\","
                : line);
    }

    [Fact]
    public async Task InvokeAsync_AheadSkillBodies_RendersAheadBlockWithAsymmetricRemediation()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        temporaryDirectory.WriteFile("Workspace.slnx", "<Solution />");
        var workspaceProvider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => temporaryDirectory.Path);
        var inventory = BuildStubInventory();
        var tools = BuildStubTools();
        var versionReport = BuildCleanVersionReport();
        var skillBodiesReport = new SkillBodiesReport(
            ManifestPath: SkillsManifest.ManifestPath,
            InstalledVersion: "0.3.0",
            ToolVersion: "0.2.1",
            DriftStatus: "ahead");
        var describeMcpState = new DescribeMcpStateTool(
            workspaceProvider,
            _ => Task.FromResult(inventory),
            StubVersionReportProvider.ForReport(versionReport),
            () => skillBodiesReport,
            () => tools);

        // Act
        var responseJson = await describeMcpState.InvokeAsync(null, CancellationToken.None);

        // Assert
        await Verify(responseJson)
            .ScrubLinesWithReplace(line => line.TrimStart().StartsWith("\"loadedSolutionPath\":")
                ? "  \"loadedSolutionPath\": \"{TEMP_SOLUTION_PATH}\","
                : line);
    }

    static HandlerInventory BuildStubInventory()
    {
        return new HandlerInventory(
        [
            new HandlerEntry(
                DeclaringTypeName: "Stub.A",
                Role: HandlerRole.CommandHandler,
                BoundContracts: [],
                DeclaringAssembly: "Stub",
                SourceLocation: null),
            new HandlerEntry(
                DeclaringTypeName: "Stub.B",
                Role: HandlerRole.EventHandler,
                BoundContracts: [],
                DeclaringAssembly: "Stub",
                SourceLocation: null),
        ]);
    }

    static McpToolDescriptor[] BuildStubTools()
    {
        return
        [
            new McpToolDescriptor("tool_one", "first", default, (_, _) => Task.FromResult("")),
            new McpToolDescriptor("tool_two", "second", default, (_, _) => Task.FromResult("")),
        ];
    }

    static EdictVersionReport BuildCleanVersionReport()
    {
        return new EdictVersionReport(
            ToolVersion: "0.1.0-preview.42",
            References:
            [
                new EdictVersionReference("Edict.Core", "0.1.0-preview.42", ["ConsumerProject"]),
            ],
            IsDrifted: false,
            HasNoEdictReferences: false,
            HasInconsistentLibraryVersions: false);
    }
}
