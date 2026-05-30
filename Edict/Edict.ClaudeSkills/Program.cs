namespace Edict.ClaudeSkills;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "install")
        {
            Console.Error.WriteLine("usage: edict-skills install [--target <path>] [--force]");
            return 1;
        }

        var targetOverride = ParseTargetOverride(args);
        var force = args.Contains("--force", StringComparer.Ordinal);
        var installer = new SkillsInstaller(
            targetOverride: targetOverride,
            currentDirectoryProvider: Directory.GetCurrentDirectory);
        var report = installer.Install(force);

        WriteSkillsReport(report);

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

    static void WriteSkillsReport(SkillsInstallReport report)
    {
        Console.Out.WriteLine($"Target: {report.TargetDirectory}");
        var manifestFileName = Path.GetFileName(report.ManifestPath);
        var versionTransition = report.PreviousInstalledVersion is null
            ? $"(no prior manifest → {report.NewInstalledVersion})"
            : $"({report.PreviousInstalledVersion} → {report.NewInstalledVersion})";
        Console.Out.WriteLine($"Manifest: {manifestFileName} {versionTransition}");
        Console.Out.WriteLine();

        Console.Out.WriteLine($"Installed: {report.Installed.Count}");
        foreach (var entry in report.Installed)
        {
            Console.Out.WriteLine($"  + {entry}");
        }
        Console.Out.WriteLine($"Refreshed: {report.Refreshed.Count}");
        foreach (var entry in report.Refreshed)
        {
            Console.Out.WriteLine($"  ~ {entry}");
        }
        Console.Out.WriteLine($"Skipped (drifted): {report.SkippedDrifted.Count}");
        foreach (var entry in report.SkippedDrifted)
        {
            Console.Out.WriteLine($"  ! {entry} — re-run with --force to overwrite (your edits will be lost)");
        }
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
