namespace Edict.Mcp.Tools;

sealed record McpToolDescriptor(
    string Name,
    string Description,
    Func<CancellationToken, Task<string>> InvokeAsync);
