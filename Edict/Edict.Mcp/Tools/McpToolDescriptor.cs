using System.Text.Json;

namespace Edict.Mcp.Tools;

sealed record McpToolDescriptor(
    string Name,
    string Description,
    JsonElement InputSchema,
    Func<IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<string>> InvokeAsync);
