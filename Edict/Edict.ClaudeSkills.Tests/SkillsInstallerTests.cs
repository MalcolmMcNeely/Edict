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
        var installer = new SkillsInstaller(
            targetOverride: null,
            currentDirectoryProvider: () => workspaceDirectory.Path);

        // Act
        var report = installer.Install();

        // Assert
        var expectedTargetDirectory = Path.Combine(workspaceDirectory.Path, ".claude", "skills");
        Assert.Equal(expectedTargetDirectory, report.TargetDirectory);
        Assert.NotEmpty(report.Installed);
        Assert.Empty(report.Skipped);
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
        var installer = new SkillsInstaller(
            targetOverride: null,
            currentDirectoryProvider: () => workspaceDirectory.Path);

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
    public void Install_WhenSkillFileAlreadyExists_ReportsSkippedAndDoesNotOverwrite()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var installer = new SkillsInstaller(
            targetOverride: null,
            currentDirectoryProvider: () => workspaceDirectory.Path);
        var firstReport = installer.Install();
        var preexistingRelativePath = firstReport.Installed.First();
        var preexistingAbsolutePath = Path.Combine(firstReport.TargetDirectory, preexistingRelativePath);
        const string preexistingContents = "hand-written contents — must not be overwritten";
        File.WriteAllText(preexistingAbsolutePath, preexistingContents);

        // Act
        var secondReport = installer.Install();

        // Assert
        Assert.Empty(secondReport.Installed);
        Assert.Contains(preexistingRelativePath, secondReport.Skipped);
        Assert.Equal(firstReport.Installed.Count, secondReport.Skipped.Count);
        Assert.Equal(preexistingContents, File.ReadAllText(preexistingAbsolutePath));
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
}
