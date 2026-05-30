using Edict.Mcp.Workspaces;

namespace Edict.Mcp.Tools;

sealed class McpToolRegistry
{
    public McpToolRegistry(MSBuildWorkspaceProvider workspaceProvider)
    {
        var describeMcpState = new DescribeMcpStateTool(workspaceProvider, () => Tools!);
        Tools =
        [
            new McpToolDescriptor(
                Name: "edict_describe_mcp_state",
                Description: "Self-diagnostic. Reports the loaded solution path, indexed-handler count, and the list of MCP tools the server has registered.",
                InvokeAsync: describeMcpState.InvokeAsync),
        ];
    }

    public IReadOnlyList<McpToolDescriptor> Tools { get; }

    public McpToolDescriptor? Find(string name)
    {
        return Tools.FirstOrDefault(tool => tool.Name == name);
    }
}
