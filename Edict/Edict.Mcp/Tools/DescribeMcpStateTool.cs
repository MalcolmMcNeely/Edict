using System.Text.Json;
using System.Text.Json.Serialization;

using Edict.Mcp.Handlers;
using Edict.Mcp.Workspaces;

namespace Edict.Mcp.Tools;

sealed class DescribeMcpStateTool
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    readonly MSBuildWorkspaceProvider workspaceProvider;
    readonly Func<CancellationToken, Task<HandlerInventory>> inventoryProvider;
    readonly Func<IReadOnlyList<McpToolDescriptor>> toolsAccessor;

    public DescribeMcpStateTool(
        MSBuildWorkspaceProvider workspaceProvider,
        Func<CancellationToken, Task<HandlerInventory>> inventoryProvider,
        Func<IReadOnlyList<McpToolDescriptor>> toolsAccessor)
    {
        this.workspaceProvider = workspaceProvider;
        this.inventoryProvider = inventoryProvider;
        this.toolsAccessor = toolsAccessor;
    }

    public async Task<string> InvokeAsync(IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        var inventory = await inventoryProvider(cancellationToken);
        var response = new DescribeMcpStateResponse(
            LoadedSolutionPath: workspaceProvider.ResolveSolutionPath(),
            IndexedHandlerCount: inventory.Handlers.Count,
            RegisteredTools: toolsAccessor()
                .Select(tool => new RegisteredToolSummary(tool.Name, tool.Description))
                .ToArray());
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    sealed record DescribeMcpStateResponse(
        string LoadedSolutionPath,
        int IndexedHandlerCount,
        IReadOnlyList<RegisteredToolSummary> RegisteredTools);

    sealed record RegisteredToolSummary(string Name, string Description);
}
