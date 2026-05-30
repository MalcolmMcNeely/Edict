using System.Text.Json;

using Edict.Mcp.Versioning;

using static VerifyXunit.Verifier;

namespace Edict.Mcp.Tests.Versioning;

public class EdictSkillsManifestInspectorTests
{
    [Fact]
    public Task Inspect_ManifestInstalledVersionMatchesToolVersion_ReturnsCurrent()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        WriteManifest(temporaryDirectory, installedVersion: "0.3.0");
        var inspector = new EdictSkillsManifestInspector(toolVersion: "0.3.0");

        // Act
        var report = inspector.Inspect(temporaryDirectory.Path);

        // Assert
        return Verify(report);
    }

    [Fact]
    public Task Inspect_ToolVersionGreaterThanInstalledVersion_ReturnsStale()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        WriteManifest(temporaryDirectory, installedVersion: "0.2.1");
        var inspector = new EdictSkillsManifestInspector(toolVersion: "0.3.0");

        // Act
        var report = inspector.Inspect(temporaryDirectory.Path);

        // Assert
        return Verify(report);
    }

    [Fact]
    public Task Inspect_ToolVersionLessThanInstalledVersion_ReturnsAhead()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        WriteManifest(temporaryDirectory, installedVersion: "0.3.0");
        var inspector = new EdictSkillsManifestInspector(toolVersion: "0.2.1");

        // Act
        var report = inspector.Inspect(temporaryDirectory.Path);

        // Assert
        return Verify(report);
    }

    [Fact]
    public Task Inspect_NoManifestFile_ReturnsMissingWithNullInstalledVersion()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        var inspector = new EdictSkillsManifestInspector(toolVersion: "0.3.0");

        // Act
        var report = inspector.Inspect(temporaryDirectory.Path);

        // Assert
        return Verify(report);
    }

    [Fact]
    public Task Inspect_PrereleaseVersionsCompareNumericallyOnPrereleaseCounter()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        WriteManifest(temporaryDirectory, installedVersion: "0.1.0-preview.9");
        var inspector = new EdictSkillsManifestInspector(toolVersion: "0.1.0-preview.10");

        // Act
        var report = inspector.Inspect(temporaryDirectory.Path);

        // Assert
        return Verify(report);
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
