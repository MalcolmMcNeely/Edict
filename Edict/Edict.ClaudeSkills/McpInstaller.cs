namespace Edict.ClaudeSkills;

public sealed class McpInstaller
{
    readonly InstallModeDetector installModeDetector;
    readonly McpJsonInspector mcpJsonInspector;
    readonly McpJsonWriter mcpJsonWriter;
    readonly Func<string> currentDirectoryProvider;

    public McpInstaller(
        InstallModeDetector installModeDetector,
        McpJsonInspector mcpJsonInspector,
        McpJsonWriter mcpJsonWriter,
        Func<string> currentDirectoryProvider)
    {
        this.installModeDetector = installModeDetector;
        this.mcpJsonInspector = mcpJsonInspector;
        this.mcpJsonWriter = mcpJsonWriter;
        this.currentDirectoryProvider = currentDirectoryProvider;
    }

    public McpInstallReport Install()
    {
        var mcpJsonPath = Path.Combine(currentDirectoryProvider(), ".mcp.json");
        var detectedMode = installModeDetector.Detect();
        var inspection = mcpJsonInspector.Inspect(mcpJsonPath, detectedMode);

        switch (inspection)
        {
            case McpJsonInspection.FileMissing:
                mcpJsonWriter.Write(mcpJsonPath, detectedMode);
                return new McpInstallReport(mcpJsonPath, detectedMode, McpInstallAction.CreatedFile, ExistingForm: null);
            case McpJsonInspection.EntryMatchesMode matches:
                return new McpInstallReport(mcpJsonPath, detectedMode, McpInstallAction.AlreadyWired, ExistingForm: matches.Mode);
            case McpJsonInspection.EntryMismatchesMode mismatch:
                return new McpInstallReport(mcpJsonPath, detectedMode, McpInstallAction.InstructionsToUpdate, ExistingForm: mismatch.CurrentForm);
            case McpJsonInspection.NoEdictEntry:
            default:
                return new McpInstallReport(mcpJsonPath, detectedMode, McpInstallAction.InstructionsToAdd, ExistingForm: null);
        }
    }
}
