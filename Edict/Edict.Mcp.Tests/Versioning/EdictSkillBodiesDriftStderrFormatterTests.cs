using Edict.Mcp.Versioning;

namespace Edict.Mcp.Tests.Versioning;

public class EdictSkillBodiesDriftStderrFormatterTests
{
    [Fact]
    public void Format_CurrentReport_ReturnsNull()
    {
        // Arrange
        var report = new SkillBodiesReport(
            ManifestPath: SkillsManifest.ManifestPath,
            InstalledVersion: "0.3.0",
            ToolVersion: "0.3.0",
            DriftStatus: "current");

        // Act
        var message = EdictSkillBodiesDriftStderrFormatter.Format(report);

        // Assert
        Assert.Null(message);
    }

    [Fact]
    public void Format_StaleReport_NamesInstalledAndToolVersions_AndRefreshRemediation()
    {
        // Arrange
        var report = new SkillBodiesReport(
            ManifestPath: SkillsManifest.ManifestPath,
            InstalledVersion: "0.2.1",
            ToolVersion: "0.3.0",
            DriftStatus: "stale");

        // Act
        var message = EdictSkillBodiesDriftStderrFormatter.Format(report);

        // Assert
        Assert.Equal(
            "edict-mcp: skill body drift detected (installed v0.2.1, tool v0.3.0). Run 'edict-skills install' to refresh.",
            message);
    }

    [Fact]
    public void Format_MissingReport_NamesManifestPath_AndBootstrapRemediation()
    {
        // Arrange
        var report = new SkillBodiesReport(
            ManifestPath: SkillsManifest.ManifestPath,
            InstalledVersion: null,
            ToolVersion: "0.3.0",
            DriftStatus: "missing");

        // Act
        var message = EdictSkillBodiesDriftStderrFormatter.Format(report);

        // Assert
        Assert.Equal(
            "edict-mcp: skill body manifest not found. Run 'edict-skills install' to install skills.",
            message);
    }

    [Fact]
    public void Format_AheadReport_NamesBothVersions_AndAsymmetricRemediation()
    {
        // Arrange
        var report = new SkillBodiesReport(
            ManifestPath: SkillsManifest.ManifestPath,
            InstalledVersion: "0.3.0",
            ToolVersion: "0.2.1",
            DriftStatus: "ahead");

        // Act
        var message = EdictSkillBodiesDriftStderrFormatter.Format(report);

        // Assert
        Assert.Equal(
            "edict-mcp: skill body manifest ahead of tool (installed v0.3.0, tool v0.2.1). Upgrade edict-skills or downgrade edict-mcp.",
            message);
    }
}
