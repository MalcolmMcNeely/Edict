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
}
