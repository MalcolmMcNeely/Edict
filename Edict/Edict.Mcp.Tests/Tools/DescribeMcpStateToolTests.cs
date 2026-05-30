using Edict.Mcp.Handlers;
using Edict.Mcp.Tools;
using Edict.Mcp.Workspaces;

using static VerifyXunit.Verifier;

namespace Edict.Mcp.Tests.Tools;

public class DescribeMcpStateToolTests
{
    [Fact]
    public async Task InvokeAsync_ReportsLoadedSolutionPath_HandlerCount_AndRegisteredTools()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        temporaryDirectory.WriteFile("Workspace.slnx", "<Solution />");
        var workspaceProvider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => temporaryDirectory.Path);
        var inventory = new HandlerInventory(
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
        var tools = new[]
        {
            new McpToolDescriptor("tool_one", "first", default, (_, _) => Task.FromResult("")),
            new McpToolDescriptor("tool_two", "second", default, (_, _) => Task.FromResult("")),
        };
        var describeMcpState = new DescribeMcpStateTool(
            workspaceProvider,
            _ => Task.FromResult(inventory),
            () => tools);

        // Act
        var responseJson = await describeMcpState.InvokeAsync(null, CancellationToken.None);

        // Assert
        await Verify(responseJson)
            .ScrubLinesWithReplace(line => line.TrimStart().StartsWith("\"loadedSolutionPath\":")
                ? "  \"loadedSolutionPath\": \"{TEMP_SOLUTION_PATH}\","
                : line);
    }
}
