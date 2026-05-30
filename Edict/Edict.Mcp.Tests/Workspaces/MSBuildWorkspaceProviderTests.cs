using Edict.Mcp.Workspaces;

using Xunit;

namespace Edict.Mcp.Tests.Workspaces;

public class MSBuildWorkspaceProviderTests
{
    [Fact]
    public void ResolveSolutionPath_WhenCurrentDirectoryHasSingleSlnx_ReturnsThatFile()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        var expectedSolutionPath = temporaryDirectory.WriteFile("Workspace.slnx", "<Solution />");
        var provider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => temporaryDirectory.Path);

        // Act
        var resolvedSolutionPath = provider.ResolveSolutionPath();

        // Assert
        Assert.Equal(expectedSolutionPath, resolvedSolutionPath);
    }

    [Fact]
    public void ResolveSolutionPath_WhenParentDirectoryHasSlnx_WalksUpAndFindsIt()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        var expectedSolutionPath = temporaryDirectory.WriteFile("Workspace.slnx", "<Solution />");
        var nestedDirectory = Path.Combine(temporaryDirectory.Path, "nested", "deeper");
        Directory.CreateDirectory(nestedDirectory);
        var provider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => nestedDirectory);

        // Act
        var resolvedSolutionPath = provider.ResolveSolutionPath();

        // Assert
        Assert.Equal(expectedSolutionPath, resolvedSolutionPath);
    }

    [Fact]
    public void ResolveSolutionPath_WhenDirectoryContainsMultipleSolutions_ThrowsAmbiguousListingAllCandidates()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        var firstSolutionPath = temporaryDirectory.WriteFile("First.slnx", "<Solution />");
        var secondSolutionPath = temporaryDirectory.WriteFile("Second.sln", "Microsoft Visual Studio Solution File");
        var provider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => temporaryDirectory.Path);

        // Act + Assert
        var exception = Assert.Throws<EdictMcpWorkspaceAmbiguousException>(() => provider.ResolveSolutionPath());
        Assert.Contains(firstSolutionPath, exception.Message);
        Assert.Contains(secondSolutionPath, exception.Message);
    }

    [Fact]
    public void ResolveSolutionPath_WhenNoSolutionAnywhereInChain_ThrowsNotFound()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        var provider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => temporaryDirectory.Path);

        // Act + Assert
        var exception = Assert.Throws<EdictMcpWorkspaceNotFoundException>(() => provider.ResolveSolutionPath());
        Assert.Contains(temporaryDirectory.Path, exception.Message);
    }

    [Fact]
    public void ResolveSolutionPath_WhenSolutionOverrideProvided_ReturnsOverrideRegardlessOfCwd()
    {
        // Arrange
        using var temporaryDirectory = new TempWorkspaceDirectory();
        var overriddenSolutionPath = temporaryDirectory.WriteFile("Override.slnx", "<Solution />");
        var unrelatedDirectory = Path.Combine(temporaryDirectory.Path, "unrelated");
        Directory.CreateDirectory(unrelatedDirectory);
        var provider = new MSBuildWorkspaceProvider(
            solutionOverride: overriddenSolutionPath,
            currentDirectoryProvider: () => unrelatedDirectory);

        // Act
        var resolvedSolutionPath = provider.ResolveSolutionPath();

        // Assert
        Assert.Equal(overriddenSolutionPath, resolvedSolutionPath);
    }
}
