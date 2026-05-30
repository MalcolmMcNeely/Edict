using System.Text.Json;
using System.Text.Json.Serialization;

using Edict.Mcp.Handlers;
using Edict.Mcp.Versioning;

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
    readonly Func<CancellationToken, Task<EdictVersionReport>> versionReportProvider;

    public ListHandlersTool(
        Func<CancellationToken, Task<HandlerInventory>> inventoryProvider,
        Func<CancellationToken, Task<EdictVersionReport>> versionReportProvider)
    {
        this.inventoryProvider = inventoryProvider;
        this.versionReportProvider = versionReportProvider;
    }

    public async Task<string> InvokeAsync(IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        var inventory = await inventoryProvider(cancellationToken);
        var versionReport = await versionReportProvider(cancellationToken);
        var response = new ListHandlersResponse(inventory.Handlers, versionReport.DriftStatus);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    sealed record ListHandlersResponse(IReadOnlyList<HandlerEntry> Handlers, string DriftStatus);
}
