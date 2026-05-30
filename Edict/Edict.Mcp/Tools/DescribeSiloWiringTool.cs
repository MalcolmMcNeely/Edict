using System.Text.Json;
using System.Text.Json.Serialization;

using Edict.Mcp.SiloWiring;

namespace Edict.Mcp.Tools;

sealed class DescribeSiloWiringTool
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    readonly Func<CancellationToken, Task<SiloWiringReport>> reportProvider;

    public DescribeSiloWiringTool(Func<CancellationToken, Task<SiloWiringReport>> reportProvider)
    {
        this.reportProvider = reportProvider;
    }

    public async Task<string> InvokeAsync(IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        var report = await reportProvider(cancellationToken);
        return JsonSerializer.Serialize(report, JsonOptions);
    }
}
