using System.Reflection;

using Xunit;

namespace Edict.ClaudeSkills.Tests;

public class SkillsInstallerTests
{
    static readonly string[] ExpectedConsumerSkillNames =
    [
        "edict-authoring",
        "edict-contracts",
        "edict-silo-wiring",
        "edict-testing",
        "edict-diagnostics",
    ];

    [Fact]
    public void Install_WhenCurrentDirectoryProvided_LandsSkillsUnderClaudeSkillsBelowCurrentDirectory()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var installer = NewInstaller(workspaceDirectory);

        // Act
        var report = installer.Install();

        // Assert
        var expectedTargetDirectory = Path.Combine(workspaceDirectory.Path, ".claude", "skills");
        Assert.Equal(expectedTargetDirectory, report.TargetDirectory);
        Assert.NotEmpty(report.Installed);
        Assert.Empty(report.Refreshed);
        Assert.Empty(report.SkippedDrifted);
        foreach (var installedRelativePath in report.Installed)
        {
            var landedPath = Path.Combine(expectedTargetDirectory, installedRelativePath);
            Assert.True(File.Exists(landedPath), $"Expected {landedPath} to exist on disk.");
            Assert.NotEmpty(File.ReadAllText(landedPath));
        }
    }

    [Fact]
    public void Install_LandsAllFiveConsumerSkills_AndDoesNotShipPlaceholder()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var installer = NewInstaller(workspaceDirectory);

        // Act
        var report = installer.Install();

        // Assert
        foreach (var skillName in ExpectedConsumerSkillNames)
        {
            var expectedRelativePath = Path.Combine(skillName, "SKILL.md");
            Assert.Contains(expectedRelativePath, report.Installed);
            var landedPath = Path.Combine(report.TargetDirectory, expectedRelativePath);
            Assert.True(File.Exists(landedPath), $"Expected {landedPath} to exist on disk.");
            Assert.NotEmpty(File.ReadAllText(landedPath));
        }
        Assert.Equal(ExpectedConsumerSkillNames.Length, report.Installed.Count);
        Assert.DoesNotContain(
            report.Installed,
            relativePath => relativePath.Contains("placeholder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Install_WhenTargetOverrideProvided_LandsSkillsInOverrideRegardlessOfCurrentDirectory()
    {
        // Arrange
        using var overrideDirectory = new TempTargetDirectory();
        using var unrelatedCurrentDirectory = new TempTargetDirectory();
        var installer = new SkillsInstaller(
            targetOverride: overrideDirectory.Path,
            currentDirectoryProvider: () => unrelatedCurrentDirectory.Path);

        // Act
        var report = installer.Install();

        // Assert
        Assert.Equal(overrideDirectory.Path, report.TargetDirectory);
        Assert.NotEmpty(report.Installed);
        foreach (var installedRelativePath in report.Installed)
        {
            var landedPath = Path.Combine(overrideDirectory.Path, installedRelativePath);
            Assert.True(File.Exists(landedPath), $"Expected {landedPath} to exist on disk.");
        }
        var unrelatedClaude = Path.Combine(unrelatedCurrentDirectory.Path, ".claude");
        Assert.False(Directory.Exists(unrelatedClaude), $"Did not expect {unrelatedClaude} to be created.");
    }

    [Fact]
    public void Install_FreshInstall_WritesManifestAndRecordsVersionTransitionFromNullToTool()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var installer = NewInstaller(workspaceDirectory, toolVersion: "1.2.3");

        // Act
        var report = installer.Install();

        // Assert
        Assert.Null(report.PreviousInstalledVersion);
        Assert.Equal("1.2.3", report.NewInstalledVersion);
        Assert.True(File.Exists(report.ManifestPath));
        Assert.EndsWith(Path.Combine(".claude", "skills", ".edict-skills-manifest.json"), report.ManifestPath);
    }

    [Fact]
    public void Install_SteadyStateNoOp_WritesNothingAndReportsEmptyBuckets()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var firstInstaller = NewInstaller(workspaceDirectory, toolVersion: "1.0.0");
        var firstReport = firstInstaller.Install();
        var beforeWriteTimes = SnapshotWriteTimes(firstReport.TargetDirectory);

        // Act
        var secondInstaller = NewInstaller(workspaceDirectory, toolVersion: "1.0.0");
        var secondReport = secondInstaller.Install();

        // Assert
        Assert.Empty(secondReport.Installed);
        Assert.Empty(secondReport.Refreshed);
        Assert.Empty(secondReport.SkippedDrifted);
        Assert.Equal("1.0.0", secondReport.PreviousInstalledVersion);
        Assert.Equal("1.0.0", secondReport.NewInstalledVersion);
        var afterWriteTimes = SnapshotWriteTimes(secondReport.TargetDirectory);
        Assert.Equal(beforeWriteTimes, afterWriteTimes);
    }

    [Fact]
    public void Install_RefreshAfterFrameworkUpdate_OverwritesUntouchedFilesAndUpdatesManifest()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var firstInstaller = NewInstaller(workspaceDirectory, toolVersion: "1.0.0");
        var firstReport = firstInstaller.Install();
        var skillName = ExpectedConsumerSkillNames[0];
        var skillFilePath = Path.Combine(firstReport.TargetDirectory, skillName, "SKILL.md");
        var originalDiskBytes = File.ReadAllBytes(skillFilePath);

        // Act — simulate a framework update by feeding mutated embedded bodies
        var mutatedBodies = InMemoryEmbeddedSkillBodies.MutateRealBodies("\n<!-- v1.1.0 update -->\n");
        var secondInstaller = NewInstaller(workspaceDirectory, embeddedBodies: mutatedBodies, toolVersion: "1.1.0");
        var secondReport = secondInstaller.Install();

        // Assert
        Assert.Empty(secondReport.Installed);
        Assert.Empty(secondReport.SkippedDrifted);
        Assert.Equal(ExpectedConsumerSkillNames.Length, secondReport.Refreshed.Count);
        Assert.Equal("1.0.0", secondReport.PreviousInstalledVersion);
        Assert.Equal("1.1.0", secondReport.NewInstalledVersion);
        var refreshedDiskBytes = File.ReadAllBytes(skillFilePath);
        Assert.NotEqual(originalDiskBytes, refreshedDiskBytes);
    }

    [Fact]
    public void Install_CustomisedFileWithoutForce_SkipsCustomisedFileButRefreshesOthers()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var firstInstaller = NewInstaller(workspaceDirectory, toolVersion: "1.0.0");
        var firstReport = firstInstaller.Install();
        var customisedSkillName = ExpectedConsumerSkillNames[0];
        var customisedRelativePath = Path.Combine(customisedSkillName, "SKILL.md");
        var customisedAbsolutePath = Path.Combine(firstReport.TargetDirectory, customisedRelativePath);
        const string customisedContents = "consumer-customised body — must not be overwritten without force";
        File.WriteAllText(customisedAbsolutePath, customisedContents);

        // Act
        var mutatedBodies = InMemoryEmbeddedSkillBodies.MutateRealBodies("\n<!-- v1.1.0 update -->\n");
        var secondInstaller = NewInstaller(workspaceDirectory, embeddedBodies: mutatedBodies, toolVersion: "1.1.0");
        var secondReport = secondInstaller.Install(force: false);

        // Assert
        Assert.Contains(customisedRelativePath, secondReport.SkippedDrifted);
        Assert.Single(secondReport.SkippedDrifted);
        Assert.Equal(ExpectedConsumerSkillNames.Length - 1, secondReport.Refreshed.Count);
        Assert.Equal(customisedContents, File.ReadAllText(customisedAbsolutePath));
    }

    [Fact]
    public void Install_CustomisedFileWithForce_OverwritesCustomisationWithoutBackup()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var firstInstaller = NewInstaller(workspaceDirectory, toolVersion: "1.0.0");
        var firstReport = firstInstaller.Install();
        var customisedSkillName = ExpectedConsumerSkillNames[0];
        var customisedAbsolutePath = Path.Combine(firstReport.TargetDirectory, customisedSkillName, "SKILL.md");
        const string customisedContents = "consumer-customised body — about to be overwritten by --force";
        File.WriteAllText(customisedAbsolutePath, customisedContents);

        // Act
        var mutatedBodies = InMemoryEmbeddedSkillBodies.MutateRealBodies("\n<!-- v1.1.0 update -->\n");
        var secondInstaller = NewInstaller(workspaceDirectory, embeddedBodies: mutatedBodies, toolVersion: "1.1.0");
        var secondReport = secondInstaller.Install(force: true);

        // Assert
        Assert.Empty(secondReport.SkippedDrifted);
        Assert.Equal(ExpectedConsumerSkillNames.Length, secondReport.Refreshed.Count);
        Assert.NotEqual(customisedContents, File.ReadAllText(customisedAbsolutePath));
        var backupPath = customisedAbsolutePath + ".bak";
        Assert.False(File.Exists(backupPath), "No backup file should be written; --force overwrites without a .bak copy.");
    }

    [Fact]
    public void Install_UpgradeBootstrap_NoManifestYetEveryFileSkippedUntilForce()
    {
        // Arrange — the no-manifest state simulating an upgrade from a pre-feature tool
        using var workspaceDirectory = new TempTargetDirectory();
        var targetDirectory = Path.Combine(workspaceDirectory.Path, ".claude", "skills");
        foreach (var skillName in ExpectedConsumerSkillNames)
        {
            workspaceDirectory.WriteFile(
                Path.Combine(".claude", "skills", skillName, "SKILL.md"),
                $"pre-feature install body for {skillName}");
        }
        Assert.False(File.Exists(Path.Combine(targetDirectory, ".edict-skills-manifest.json")));

        // Act — plain install first
        var plainReport = NewInstaller(workspaceDirectory, toolVersion: "1.1.0").Install(force: false);

        // Assert plain install
        Assert.Empty(plainReport.Installed);
        Assert.Empty(plainReport.Refreshed);
        Assert.Equal(ExpectedConsumerSkillNames.Length, plainReport.SkippedDrifted.Count);
        Assert.Null(plainReport.PreviousInstalledVersion);

        // Act — re-run with --force
        var forcedReport = NewInstaller(workspaceDirectory, toolVersion: "1.1.0").Install(force: true);

        // Assert forced run
        Assert.Empty(forcedReport.Installed);
        Assert.Empty(forcedReport.SkippedDrifted);
        Assert.Equal(ExpectedConsumerSkillNames.Length, forcedReport.Refreshed.Count);
        Assert.True(File.Exists(Path.Combine(targetDirectory, ".edict-skills-manifest.json")));
    }

    static SkillsInstaller NewInstaller(TempTargetDirectory workspaceDirectory, string toolVersion = "0.0.0-test")
    {
        return new SkillsInstaller(
            targetOverride: null,
            currentDirectoryProvider: () => workspaceDirectory.Path,
            resourceAssembly: typeof(SkillsInstaller).Assembly,
            toolVersion: toolVersion);
    }

    static SkillsInstaller NewInstaller(
        TempTargetDirectory workspaceDirectory,
        IReadOnlyList<SkillsInstaller.EmbeddedSkillBody> embeddedBodies,
        string toolVersion = "0.0.0-test")
    {
        return new SkillsInstaller(
            targetOverride: null,
            currentDirectoryProvider: () => workspaceDirectory.Path,
            embeddedBodiesProvider: () => embeddedBodies,
            toolVersion: toolVersion);
    }

    static Dictionary<string, DateTime> SnapshotWriteTimes(string targetDirectory)
    {
        return Directory.EnumerateFiles(targetDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => File.GetLastWriteTimeUtc(path));
    }
}
