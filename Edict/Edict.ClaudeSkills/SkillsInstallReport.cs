namespace Edict.ClaudeSkills;

public sealed record SkillsInstallReport(
    string TargetDirectory,
    IReadOnlyList<string> Installed,
    IReadOnlyList<string> Refreshed,
    IReadOnlyList<string> SkippedDrifted,
    string ManifestPath,
    string? PreviousInstalledVersion,
    string NewInstalledVersion);
