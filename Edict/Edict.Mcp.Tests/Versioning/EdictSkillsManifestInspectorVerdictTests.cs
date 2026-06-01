using System.Text.Json;

using Edict.Mcp.Versioning;

using Xunit;

namespace Edict.Mcp.Tests.Versioning;

// The drift verdict the MCP server reports to the consumer turns on a semver
// comparison between the installed skills and the running tool. These pin the
// edges that decide that verdict: release outranks prerelease, prerelease
// identifiers order numerically then ordinally, and a longer prerelease run
// outranks its prefix.
public class EdictSkillsManifestInspectorVerdictTests
{
    [Theory]
    // Core version ordering.
    [InlineData("2.0.0", "1.9.9", "stale")]
    [InlineData("1.0.0", "1.0.1", "ahead")]
    [InlineData("1.2.3", "1.2.3", "current")]
    // Release outranks an otherwise-equal prerelease.
    [InlineData("1.0.0", "1.0.0-rc", "stale")]
    [InlineData("1.0.0-rc", "1.0.0", "ahead")]
    // Numeric prerelease identifiers compare as numbers, not strings.
    [InlineData("1.0.0-rc.2", "1.0.0-rc.10", "ahead")]
    // A longer prerelease run outranks the prefix it extends.
    [InlineData("1.0.0-rc.1", "1.0.0-rc", "stale")]
    [InlineData("1.0.0-rc", "1.0.0-rc.1", "ahead")]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.1", "current")]
    // A numeric identifier outranks an alphanumeric one at the same position.
    [InlineData("1.0.0-1", "1.0.0-rc", "ahead")]
    [InlineData("1.0.0-rc", "1.0.0-1", "stale")]
    // Two alphanumeric identifiers fall back to ordinal comparison.
    [InlineData("1.0.0-alpha", "1.0.0-beta", "ahead")]
    public void Inspect_ClassifiesDrift_FromSemverComparison(string toolVersion, string installedVersion, string expectedDriftStatus)
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        WriteManifest(temporaryDirectory, installedVersion);
        var inspector = new EdictSkillsManifestInspector(toolVersion);

        // Act
        var report = inspector.Inspect(temporaryDirectory.Path);

        // Assert
        Assert.Equal(expectedDriftStatus, report.DriftStatus);
        Assert.Equal(installedVersion, report.InstalledVersion);
    }

    [Fact]
    public void Inspect_UnparseableManifest_DegradesToMissing()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        temporaryDirectory.WriteFile(SkillsManifest.ManifestPath, "{ not valid json");
        var inspector = new EdictSkillsManifestInspector(toolVersion: "1.0.0");

        // Act
        var report = inspector.Inspect(temporaryDirectory.Path);

        // Assert
        Assert.Equal("missing", report.DriftStatus);
        Assert.Null(report.InstalledVersion);
    }

    [Fact]
    public void Inspect_ManifestDeserialisingToNull_DegradesToMissing()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        temporaryDirectory.WriteFile(SkillsManifest.ManifestPath, "null");
        var inspector = new EdictSkillsManifestInspector(toolVersion: "1.0.0");

        // Act
        var report = inspector.Inspect(temporaryDirectory.Path);

        // Assert
        Assert.Equal("missing", report.DriftStatus);
        Assert.Null(report.InstalledVersion);
    }

    static void WriteManifest(TempWorkspaceDirectory temporaryDirectory, string installedVersion)
    {
        var manifest = new SkillsManifest(
            InstalledVersion: installedVersion,
            Skills: new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["edict-authoring"] = "sha256:0000000000000000000000000000000000000000000000000000000000000001",
            });
        var json = JsonSerializer.Serialize(manifest, SkillsManifest.SerializerOptions);
        temporaryDirectory.WriteFile(SkillsManifest.ManifestPath, json);
    }
}
