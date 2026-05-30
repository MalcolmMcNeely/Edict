using System.Reflection;
using System.Text.Json;

namespace Edict.Mcp.Versioning;

sealed class EdictSkillsManifestInspector
{
    readonly string toolVersion;

    public EdictSkillsManifestInspector()
        : this(ResolveToolVersion())
    {
    }

    internal EdictSkillsManifestInspector(string toolVersion)
    {
        this.toolVersion = toolVersion;
    }

    public SkillBodiesReport Inspect(string workspaceRoot)
    {
        var manifestAbsolutePath = Path.Combine(workspaceRoot, SkillsManifest.ManifestPath);
        if (!File.Exists(manifestAbsolutePath))
        {
            return new SkillBodiesReport(
                ManifestPath: SkillsManifest.ManifestPath,
                InstalledVersion: null,
                ToolVersion: toolVersion,
                DriftStatus: "missing");
        }

        SkillsManifest? manifest;
        try
        {
            var manifestJson = File.ReadAllText(manifestAbsolutePath);
            manifest = JsonSerializer.Deserialize<SkillsManifest>(manifestJson, SkillsManifest.SerializerOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new SkillBodiesReport(
                ManifestPath: SkillsManifest.ManifestPath,
                InstalledVersion: null,
                ToolVersion: toolVersion,
                DriftStatus: "missing");
        }

        if (manifest is null)
        {
            return new SkillBodiesReport(
                ManifestPath: SkillsManifest.ManifestPath,
                InstalledVersion: null,
                ToolVersion: toolVersion,
                DriftStatus: "missing");
        }

        var driftStatus = ClassifyDrift(toolVersion, manifest.InstalledVersion);
        return new SkillBodiesReport(
            ManifestPath: SkillsManifest.ManifestPath,
            InstalledVersion: manifest.InstalledVersion,
            ToolVersion: toolVersion,
            DriftStatus: driftStatus);
    }

    static string ClassifyDrift(string toolVersion, string installedVersion)
    {
        var comparison = CompareVersions(toolVersion, installedVersion);
        if (comparison == 0)
        {
            return "current";
        }
        return comparison > 0 ? "stale" : "ahead";
    }

    static int CompareVersions(string left, string right)
    {
        var (leftCore, leftPrerelease) = SplitVersion(left);
        var (rightCore, rightPrerelease) = SplitVersion(right);

        var coreComparison = CompareNumericParts(leftCore, rightCore);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        if (leftPrerelease is null && rightPrerelease is null)
        {
            return 0;
        }
        if (leftPrerelease is null)
        {
            return 1;
        }
        if (rightPrerelease is null)
        {
            return -1;
        }
        return ComparePrereleaseParts(leftPrerelease, rightPrerelease);
    }

    static (string Core, string? Prerelease) SplitVersion(string version)
    {
        var dashIndex = version.IndexOf('-');
        return dashIndex < 0
            ? (version, null)
            : (version[..dashIndex], version[(dashIndex + 1)..]);
    }

    static int CompareNumericParts(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var maxLength = Math.Max(leftParts.Length, rightParts.Length);
        for (var index = 0; index < maxLength; index++)
        {
            var leftValue = index < leftParts.Length && int.TryParse(leftParts[index], out var leftInt) ? leftInt : 0;
            var rightValue = index < rightParts.Length && int.TryParse(rightParts[index], out var rightInt) ? rightInt : 0;
            var comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }
        return 0;
    }

    static int ComparePrereleaseParts(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var maxLength = Math.Max(leftParts.Length, rightParts.Length);
        for (var index = 0; index < maxLength; index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }
            if (index >= rightParts.Length)
            {
                return 1;
            }
            var comparison = ComparePrereleaseIdentifier(leftParts[index], rightParts[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }
        return 0;
    }

    static int ComparePrereleaseIdentifier(string left, string right)
    {
        var leftIsNumeric = int.TryParse(left, out var leftInt);
        var rightIsNumeric = int.TryParse(right, out var rightInt);
        if (leftIsNumeric && rightIsNumeric)
        {
            return leftInt.CompareTo(rightInt);
        }
        if (leftIsNumeric)
        {
            return -1;
        }
        if (rightIsNumeric)
        {
            return 1;
        }
        return string.CompareOrdinal(left, right);
    }

    static string ResolveToolVersion()
    {
        var attribute = typeof(EdictSkillsManifestInspector).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attribute?.InformationalVersion ?? "unknown";
    }
}
