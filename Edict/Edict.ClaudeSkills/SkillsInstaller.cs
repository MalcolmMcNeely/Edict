using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Edict.ClaudeSkills;

public sealed class SkillsInstaller
{
    const string EmbeddedResourcePrefix = "Edict.ClaudeSkills.Skills.";
    const string ManifestFileName = ".edict-skills-manifest.json";

    readonly string? targetOverride;
    readonly Func<string> currentDirectoryProvider;
    readonly Func<IReadOnlyList<EmbeddedSkillBody>> embeddedBodiesProvider;
    readonly string toolVersion;

    public SkillsInstaller(string? targetOverride, Func<string> currentDirectoryProvider)
        : this(
            targetOverride,
            currentDirectoryProvider,
            () => LoadFromAssembly(typeof(SkillsInstaller).Assembly),
            ReadInformationalVersion(typeof(SkillsInstaller).Assembly))
    {
    }

    internal SkillsInstaller(string? targetOverride, Func<string> currentDirectoryProvider, Assembly resourceAssembly, string toolVersion)
        : this(targetOverride, currentDirectoryProvider, () => LoadFromAssembly(resourceAssembly), toolVersion)
    {
    }

    internal SkillsInstaller(
        string? targetOverride,
        Func<string> currentDirectoryProvider,
        Func<IReadOnlyList<EmbeddedSkillBody>> embeddedBodiesProvider,
        string toolVersion)
    {
        this.targetOverride = targetOverride;
        this.currentDirectoryProvider = currentDirectoryProvider;
        this.embeddedBodiesProvider = embeddedBodiesProvider;
        this.toolVersion = toolVersion;
    }

    public string ResolveTargetDirectory()
    {
        if (targetOverride is not null)
        {
            return Path.GetFullPath(targetOverride);
        }
        return Path.Combine(currentDirectoryProvider(), ".claude", "skills");
    }

    public SkillsInstallReport Install() => Install(force: false);

    public SkillsInstallReport Install(bool force)
    {
        var targetDirectory = ResolveTargetDirectory();
        Directory.CreateDirectory(targetDirectory);

        var manifestAbsolutePath = Path.Combine(targetDirectory, ManifestFileName);
        var previousManifest = TryReadManifest(manifestAbsolutePath);

        var embeddedBodies = embeddedBodiesProvider();
        var embeddedDescriptors = embeddedBodies
            .Select(body => new EmbeddedSkillDescriptor(body.SkillName, body.ContentHash))
            .ToList();
        var onDiskDescriptors = LoadOnDiskDescriptors(targetDirectory, embeddedBodies);

        var decisions = SkillsDriftEvaluator.Evaluate(
            currentManifest: previousManifest,
            embeddedSkills: embeddedDescriptors,
            onDiskSkills: onDiskDescriptors,
            force: force);

        var installed = new List<string>();
        var refreshed = new List<string>();
        var skippedDrifted = new List<string>();
        var nextSkills = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var diskByName = onDiskDescriptors.ToDictionary(descriptor => descriptor.Name, descriptor => descriptor.ContentHash, StringComparer.Ordinal);

        foreach (var body in embeddedBodies)
        {
            var relativePath = Path.Combine(body.SkillName, "SKILL.md");
            var skillDirectory = Path.Combine(targetDirectory, body.SkillName);
            var skillFilePath = Path.Combine(skillDirectory, "SKILL.md");
            var decision = decisions[body.SkillName];

            switch (decision)
            {
                case SkillDriftDecision.Create:
                    Directory.CreateDirectory(skillDirectory);
                    File.WriteAllBytes(skillFilePath, body.Content);
                    installed.Add(relativePath);
                    nextSkills[body.SkillName] = body.ContentHash;
                    break;
                case SkillDriftDecision.Refresh:
                    if (!diskByName.TryGetValue(body.SkillName, out var diskHash) || !string.Equals(diskHash, body.ContentHash, StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(skillDirectory);
                        File.WriteAllBytes(skillFilePath, body.Content);
                        refreshed.Add(relativePath);
                    }
                    nextSkills[body.SkillName] = body.ContentHash;
                    break;
                case SkillDriftDecision.SkipDrifted:
                    skippedDrifted.Add(relativePath);
                    if (previousManifest is not null && previousManifest.Skills.TryGetValue(body.SkillName, out var priorHash))
                    {
                        nextSkills[body.SkillName] = priorHash;
                    }
                    break;
            }
        }

        var anyWrites = installed.Count > 0 || refreshed.Count > 0;
        var newManifestVersion = anyWrites ? toolVersion : previousManifest?.InstalledVersion ?? toolVersion;
        var manifestNeedsWrite = anyWrites
            || previousManifest is null
            || !ManifestSkillsMatch(previousManifest.Skills, nextSkills);

        if (manifestNeedsWrite)
        {
            var nextManifest = new SkillsManifest(newManifestVersion, nextSkills);
            WriteManifest(manifestAbsolutePath, nextManifest);
        }

        return new SkillsInstallReport(
            TargetDirectory: targetDirectory,
            Installed: installed,
            Refreshed: refreshed,
            SkippedDrifted: skippedDrifted,
            ManifestPath: manifestAbsolutePath,
            PreviousInstalledVersion: previousManifest?.InstalledVersion,
            NewInstalledVersion: newManifestVersion);
    }

    static bool ManifestSkillsMatch(IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> next)
    {
        if (previous.Count != next.Count)
        {
            return false;
        }
        foreach (var pair in previous)
        {
            if (!next.TryGetValue(pair.Key, out var nextValue) || !string.Equals(pair.Value, nextValue, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    static SkillsManifest? TryReadManifest(string manifestAbsolutePath)
    {
        if (!File.Exists(manifestAbsolutePath))
        {
            return null;
        }
        var contents = File.ReadAllText(manifestAbsolutePath);
        return JsonSerializer.Deserialize<SkillsManifest>(contents, SkillsManifest.SerializerOptions);
    }

    static void WriteManifest(string manifestAbsolutePath, SkillsManifest manifest)
    {
        var parent = Path.GetDirectoryName(manifestAbsolutePath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
        var serialised = JsonSerializer.Serialize(manifest, SkillsManifest.SerializerOptions);
        File.WriteAllText(manifestAbsolutePath, serialised);
    }

    static IReadOnlyList<EmbeddedSkillBody> LoadFromAssembly(Assembly assembly)
    {
        var bodies = new List<EmbeddedSkillBody>();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(EmbeddedResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }
            using var resourceStream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded skill resource '{resourceName}' could not be opened.");
            using var memory = new MemoryStream();
            resourceStream.CopyTo(memory);
            var content = memory.ToArray();
            var skillFileName = resourceName[EmbeddedResourcePrefix.Length..];
            var skillName = Path.GetFileNameWithoutExtension(skillFileName);
            bodies.Add(new EmbeddedSkillBody(skillName, content, ComputeHash(content)));
        }
        return bodies;
    }

    internal static EmbeddedSkillBody MakeEmbeddedSkillBody(string skillName, byte[] content)
    {
        return new EmbeddedSkillBody(skillName, content, ComputeHash(content));
    }

    static List<OnDiskSkillDescriptor> LoadOnDiskDescriptors(string targetDirectory, IReadOnlyList<EmbeddedSkillBody> embeddedBodies)
    {
        var descriptors = new List<OnDiskSkillDescriptor>();
        foreach (var body in embeddedBodies)
        {
            var skillFilePath = Path.Combine(targetDirectory, body.SkillName, "SKILL.md");
            if (!File.Exists(skillFilePath))
            {
                continue;
            }
            var diskBytes = File.ReadAllBytes(skillFilePath);
            descriptors.Add(new OnDiskSkillDescriptor(body.SkillName, ComputeHash(diskBytes)));
        }
        return descriptors;
    }

    static string ComputeHash(byte[] content)
    {
        var hashBytes = SHA256.HashData(content);
        return "sha256:" + Convert.ToHexStringLower(hashBytes);
    }

    static string ReadInformationalVersion(Assembly assembly)
    {
        var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attribute?.InformationalVersion ?? "0.0.0";
    }

    internal sealed record EmbeddedSkillBody(string SkillName, byte[] Content, string ContentHash);
}
