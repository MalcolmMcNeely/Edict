using Edict.Mcp.Tools;
using Edict.Mcp.Workspaces;

using Xunit;

namespace Edict.Mcp.Tests.Tools;

public class McpToolRegistryTests
{
    [Fact]
    public void Tools_IncludesDescribeMcpStateMetaTool()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        temporaryDirectory.WriteFile("Workspace.slnx", "<Solution />");
        var workspaceProvider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => temporaryDirectory.Path);
        var registry = new McpToolRegistry(workspaceProvider);

        // Act
        var describeMcpState = registry.Tools.SingleOrDefault(tool => tool.Name == "edict_describe_mcp_state");

        // Assert
        Assert.NotNull(describeMcpState);
        Assert.False(string.IsNullOrWhiteSpace(describeMcpState!.Description));
    }
}
