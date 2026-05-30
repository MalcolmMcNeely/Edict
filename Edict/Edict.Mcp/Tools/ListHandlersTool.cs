using System.Text.Json;
using System.Text.Json.Serialization;

using Edict.Mcp.Handlers;

namespace Edict.Mcp.Tools;

sealed class ListHandlersTool
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    readonly Func<CancellationToken, Task<HandlerInventory>> inventoryProvider;

    public ListHandlersTool(Func<CancellationToken, Task<HandlerInventory>> inventoryProvider)
    {
        this.inventoryProvider = inventoryProvider;
    }

    public async Task<string> InvokeAsync(IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        var inventory = await inventoryProvider(cancellationToken);
        return JsonSerializer.Serialize(inventory, JsonOptions);
    }
}
