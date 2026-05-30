namespace Edict.ClaudeSkills;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "install")
        {
            Console.Error.WriteLine("usage: edict-skills install [--target <path>]");
            return 1;
        }

        var targetOverride = ParseTargetOverride(args);
        var installer = new SkillsInstaller(
            targetOverride: targetOverride,
            currentDirectoryProvider: Directory.GetCurrentDirectory);
        var report = installer.Install();

        Console.Out.WriteLine($"Target: {report.TargetDirectory}");
        Console.Out.WriteLine($"Installed: {report.Installed.Count}");
        foreach (var installed in report.Installed)
        {
            Console.Out.WriteLine($"  + {installed}");
        }
        Console.Out.WriteLine($"Skipped: {report.Skipped.Count}");
        foreach (var skipped in report.Skipped)
        {
            Console.Out.WriteLine($"  = {skipped}");
        }

        var mcpInstaller = new McpInstaller(
            installModeDetector: new InstallModeDetector(),
            mcpJsonInspector: new McpJsonInspector(),
            mcpJsonWriter: new McpJsonWriter(),
            currentDirectoryProvider: Directory.GetCurrentDirectory);
        var mcpReport = mcpInstaller.Install();
        WriteMcpReport(mcpReport);

        return 0;
    }

    static string? ParseTargetOverride(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == "--target")
            {
                return args[index + 1];
            }
        }
        return null;
    }

    static void WriteMcpReport(McpInstallReport report)
    {
        Console.Out.WriteLine($"MCP: {report.McpJsonPath}");
        Console.Out.WriteLine($"  Detected install mode: {report.DetectedMode}");
        switch (report.Action)
        {
            case McpInstallAction.CreatedFile:
                Console.Out.WriteLine($"  + Created {Path.GetFileName(report.McpJsonPath)} with the edict entry for {report.DetectedMode} mode.");
                break;
            case McpInstallAction.AlreadyWired:
                Console.Out.WriteLine($"  = {Path.GetFileName(report.McpJsonPath)} already wired for {report.DetectedMode} mode.");
                break;
            case McpInstallAction.InstructionsToAdd:
                Console.Out.WriteLine($"  ! {Path.GetFileName(report.McpJsonPath)} has no \"edict\" entry under mcpServers. Add this entry:");
                Console.Error.WriteLine(BuildEdictEntrySnippet(report.DetectedMode));
                break;
            case McpInstallAction.InstructionsToUpdate:
                Console.Out.WriteLine($"  ! {Path.GetFileName(report.McpJsonPath)} has an \"edict\" entry in {report.ExistingForm} form but {report.DetectedMode} form was detected. Update the entry to:");
                Console.Error.WriteLine(BuildEdictEntrySnippet(report.DetectedMode));
                break;
        }
    }

    static string BuildEdictEntrySnippet(InstallMode installMode)
    {
        if (installMode == InstallMode.Manifest)
        {
            return """
                "edict": {
                  "command": "dotnet",
                  "args": ["edict-mcp"]
                }
                """;
        }
        return """
            "edict": {
              "command": "edict-mcp"
            }
            """;
    }
}
