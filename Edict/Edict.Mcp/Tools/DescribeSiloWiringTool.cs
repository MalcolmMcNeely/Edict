using System.Text.Json;
using System.Text.Json.Serialization;

using Edict.Mcp.Handlers;
using Edict.Mcp.SiloWiring;
using Edict.Mcp.Versioning;

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
    readonly Func<CancellationToken, Task<EdictVersionReport>> versionReportProvider;

    public DescribeSiloWiringTool(
        Func<CancellationToken, Task<SiloWiringReport>> reportProvider,
        Func<CancellationToken, Task<EdictVersionReport>> versionReportProvider)
    {
        this.reportProvider = reportProvider;
        this.versionReportProvider = versionReportProvider;
    }

    public async Task<string> InvokeAsync(IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        var report = await reportProvider(cancellationToken);
        var versionReport = await versionReportProvider(cancellationToken);
        var response = new DescribeSiloWiringResponse(
            report.ProgramSourceLocation,
            report.Wired,
            report.Missing,
            versionReport.DriftStatus);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    sealed record DescribeSiloWiringResponse(
        SourceLocationInfo? ProgramSourceLocation,
        IReadOnlyList<SiloWiringEntry> Wired,
        IReadOnlyList<SiloWiringEntry> Missing,
        string DriftStatus);
}
