namespace Edict.ClaudeSkills;

public sealed record SkillsInstallReport(
    string TargetDirectory,
    IReadOnlyList<string> Installed,
    IReadOnlyList<string> Skipped);
