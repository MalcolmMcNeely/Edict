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
        var registry = BuildRegistry();

        // Act
        var describeMcpState = registry.Tools.SingleOrDefault(tool => tool.Name == "edict_describe_mcp_state");

        // Assert
        Assert.NotNull(describeMcpState);
        Assert.False(string.IsNullOrWhiteSpace(describeMcpState!.Description));
    }

    [Theory]
    [InlineData("edict_describe_glossary_term")]
    [InlineData("edict_lookup_adr")]
    [InlineData("edict_list_handlers")]
    [InlineData("edict_list_route_keys")]
    [InlineData("edict_describe_silo_wiring")]
    public void Tools_IncludesDocsTool(string toolName)
    {
        // Arrange
        var registry = BuildRegistry();

        // Act
        var tool = registry.Tools.SingleOrDefault(descriptor => descriptor.Name == toolName);

        // Assert
        Assert.NotNull(tool);
        Assert.False(string.IsNullOrWhiteSpace(tool!.Description));
    }

    static McpToolRegistry BuildRegistry()
    {
        var temporaryDirectory = new TempWorkspaceDirectory();
        temporaryDirectory.WriteFile("Workspace.slnx", "<Solution />");
        var workspaceProvider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => temporaryDirectory.Path);
        return new McpToolRegistry(workspaceProvider);
    }
}
