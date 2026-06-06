using System.Text.Json;
using System.Text.Json.Serialization;

using Edict.Mcp.Configuration;

namespace Edict.Mcp.Tools;

sealed class CheckConfigurationTool
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    readonly Func<CancellationToken, Task<ConfigurationCheckReport>> reportProvider;

    public CheckConfigurationTool(Func<CancellationToken, Task<ConfigurationCheckReport>> reportProvider)
    {
        this.reportProvider = reportProvider;
    }

    public async Task<string> InvokeAsync(IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        var report = await reportProvider(cancellationToken);
        return JsonSerializer.Serialize(report, JsonOptions);
    }
}
