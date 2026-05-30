namespace Edict.ClaudeSkills;

public sealed record McpInstallReport(
    string McpJsonPath,
    InstallMode DetectedMode,
    McpInstallAction Action,
    InstallMode? ExistingForm);
