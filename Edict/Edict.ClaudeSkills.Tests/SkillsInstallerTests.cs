using Xunit;

namespace Edict.ClaudeSkills.Tests;

public class SkillsInstallerTests
{
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
    public void Install_WhenSkillFileAlreadyExists_ReportsSkippedAndDoesNotOverwrite()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var installer = new SkillsInstaller(
            targetOverride: null,
            currentDirectoryProvider: () => workspaceDirectory.Path);
        var firstReport = installer.Install();
        var preexistingRelativePath = firstReport.Installed.Single();
        var preexistingAbsolutePath = Path.Combine(firstReport.TargetDirectory, preexistingRelativePath);
        const string preexistingContents = "hand-written contents — must not be overwritten";
        File.WriteAllText(preexistingAbsolutePath, preexistingContents);

        // Act
        var secondReport = installer.Install();

        // Assert
        Assert.Empty(secondReport.Installed);
        Assert.Equal([preexistingRelativePath], secondReport.Skipped);
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
