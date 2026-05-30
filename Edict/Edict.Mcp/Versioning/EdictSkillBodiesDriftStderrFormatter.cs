namespace Edict.Mcp.Versioning;

static class EdictSkillBodiesDriftStderrFormatter
{
    public static string? Format(SkillBodiesReport report)
    {
        return report.DriftStatus switch
        {
            "current" => null,
            "stale" => $"edict-mcp: skill body drift detected (installed v{report.InstalledVersion}, tool v{report.ToolVersion}). Run 'edict-skills install' to refresh.",
            "missing" => "edict-mcp: skill body manifest not found. Run 'edict-skills install' to install skills.",
            "ahead" => $"edict-mcp: skill body manifest ahead of tool (installed v{report.InstalledVersion}, tool v{report.ToolVersion}). Upgrade edict-skills or downgrade edict-mcp.",
            _ => null,
        };
    }
}
