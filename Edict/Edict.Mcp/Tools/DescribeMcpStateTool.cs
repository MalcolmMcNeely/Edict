using System.Text.Json;
using System.Text.Json.Serialization;

using Edict.Mcp.Handlers;
using Edict.Mcp.Versioning;
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
    readonly Func<CancellationToken, Task<EdictVersionReport>> versionReportProvider;
    readonly Func<SkillBodiesReport> skillBodiesProvider;
    readonly Func<IReadOnlyList<McpToolDescriptor>> toolsAccessor;

    public DescribeMcpStateTool(
        MSBuildWorkspaceProvider workspaceProvider,
        Func<CancellationToken, Task<HandlerInventory>> inventoryProvider,
        Func<CancellationToken, Task<EdictVersionReport>> versionReportProvider,
        Func<SkillBodiesReport> skillBodiesProvider,
        Func<IReadOnlyList<McpToolDescriptor>> toolsAccessor)
    {
        this.workspaceProvider = workspaceProvider;
        this.inventoryProvider = inventoryProvider;
        this.versionReportProvider = versionReportProvider;
        this.skillBodiesProvider = skillBodiesProvider;
        this.toolsAccessor = toolsAccessor;
    }

    public async Task<string> InvokeAsync(IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        var inventory = await inventoryProvider(cancellationToken);
        var versionReport = await versionReportProvider(cancellationToken);
        var skillBodiesReport = skillBodiesProvider();
        var response = new DescribeMcpStateResponse(
            LoadedSolutionPath: workspaceProvider.ResolveSolutionPath(),
            IndexedHandlerCount: inventory.Handlers.Count,
            EdictVersionReport: versionReport,
            SkillBodies: BuildSkillBodiesView(skillBodiesReport),
            RegisteredTools: toolsAccessor()
                .Select(tool => new RegisteredToolSummary(tool.Name, tool.Description))
                .ToArray());
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    static SkillBodiesView BuildSkillBodiesView(SkillBodiesReport report)
    {
        return new SkillBodiesView(
            ManifestPath: report.ManifestPath,
            InstalledVersion: report.InstalledVersion,
            ToolVersion: report.ToolVersion,
            DriftStatus: report.DriftStatus,
            Remediation: ResolveRemediation(report.DriftStatus));
    }

    static string? ResolveRemediation(string driftStatus)
    {
        return driftStatus switch
        {
            "stale" => "edict-skills install (then --force if drift warnings appear)",
            "missing" => "edict-skills install",
            "ahead" => "upgrade edict-skills or downgrade edict-mcp",
            _ => null,
        };
    }

    sealed record DescribeMcpStateResponse(
        string LoadedSolutionPath,
        int IndexedHandlerCount,
        EdictVersionReport EdictVersionReport,
        SkillBodiesView SkillBodies,
        IReadOnlyList<RegisteredToolSummary> RegisteredTools);

    sealed record SkillBodiesView(
        string ManifestPath,
        string? InstalledVersion,
        string ToolVersion,
        string DriftStatus,
        string? Remediation);

    sealed record RegisteredToolSummary(string Name, string Description);
}
