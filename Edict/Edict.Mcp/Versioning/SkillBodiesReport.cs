namespace Edict.Mcp.Versioning;

sealed record SkillBodiesReport(
    string ManifestPath,
    string? InstalledVersion,
    string ToolVersion,
    string DriftStatus);
