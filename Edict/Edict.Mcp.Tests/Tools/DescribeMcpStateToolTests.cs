using System.Threading.Tasks;

using Edict.Mcp.Tools;
using Edict.Mcp.Workspaces;

using static VerifyXunit.Verifier;

namespace Edict.Mcp.Tests.Tools;

public class DescribeMcpStateToolTests
{
    [Fact]
    public async Task InvokeAsync_ReportsLoadedSolutionPathZeroHandlersAndRegisteredToolList()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        temporaryDirectory.WriteFile("Workspace.slnx", "<Solution />");
        var workspaceProvider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => temporaryDirectory.Path);
        var registry = new McpToolRegistry(workspaceProvider);
        var describeMcpState = registry.Find("edict_describe_mcp_state")!;

        // Act
        var responseJson = await describeMcpState.InvokeAsync(null, CancellationToken.None);

        // Assert
        await Verify(responseJson)
            .ScrubLinesWithReplace(line => line.TrimStart().StartsWith("\"loadedSolutionPath\":")
                ? "  \"loadedSolutionPath\": \"{TEMP_SOLUTION_PATH}\","
                : line);
    }
}
