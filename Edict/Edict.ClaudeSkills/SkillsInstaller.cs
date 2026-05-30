using System.Reflection;

namespace Edict.ClaudeSkills;

public sealed class SkillsInstaller
{
    const string EmbeddedResourcePrefix = "Edict.ClaudeSkills.Skills.";

    readonly string? targetOverride;
    readonly Func<string> currentDirectoryProvider;
    readonly Assembly resourceAssembly;

    public SkillsInstaller(string? targetOverride, Func<string> currentDirectoryProvider)
        : this(targetOverride, currentDirectoryProvider, typeof(SkillsInstaller).Assembly)
    {
    }

    internal SkillsInstaller(string? targetOverride, Func<string> currentDirectoryProvider, Assembly resourceAssembly)
    {
        this.targetOverride = targetOverride;
        this.currentDirectoryProvider = currentDirectoryProvider;
        this.resourceAssembly = resourceAssembly;
    }

    public string ResolveTargetDirectory()
    {
        if (targetOverride is not null)
        {
            return Path.GetFullPath(targetOverride);
        }
        return Path.Combine(currentDirectoryProvider(), ".claude", "skills");
    }

    public SkillsInstallReport Install()
    {
        var targetDirectory = ResolveTargetDirectory();
        Directory.CreateDirectory(targetDirectory);

        var installed = new List<string>();
        var skipped = new List<string>();

        foreach (var (resourceName, skillFileName) in EnumerateEmbeddedSkills())
        {
            var skillName = Path.GetFileNameWithoutExtension(skillFileName);
            var skillDirectory = Path.Combine(targetDirectory, skillName);
            var skillFilePath = Path.Combine(skillDirectory, "SKILL.md");
            var relativePath = Path.Combine(skillName, "SKILL.md");

            if (File.Exists(skillFilePath))
            {
                skipped.Add(relativePath);
                continue;
            }

            Directory.CreateDirectory(skillDirectory);
            using var resourceStream = resourceAssembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded skill resource '{resourceName}' could not be opened.");
            using var fileStream = File.Create(skillFilePath);
            resourceStream.CopyTo(fileStream);
            installed.Add(relativePath);
        }

        return new SkillsInstallReport(
            TargetDirectory: targetDirectory,
            Installed: installed,
            Skipped: skipped);
    }

    IEnumerable<(string ResourceName, string SkillFileName)> EnumerateEmbeddedSkills()
    {
        foreach (var resourceName in resourceAssembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(EmbeddedResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }
            var skillFileName = resourceName[EmbeddedResourcePrefix.Length..];
            yield return (resourceName, skillFileName);
        }
    }
}
