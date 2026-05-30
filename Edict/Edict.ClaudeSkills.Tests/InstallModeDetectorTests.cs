using Xunit;

namespace Edict.ClaudeSkills.Tests;

public class InstallModeDetectorTests
{
    [Fact]
    public void Detect_WhenAssemblyPathIsUnderGlobalToolsStore_ReturnsGlobal()
    {
        // Arrange
        var globalToolPath = @"C:\Users\someuser\.dotnet\tools\.store\edict.claudeskills\0.1.0-preview.1\edict.claudeskills\0.1.0-preview.1\tools\net10.0\any\edict-skills.dll";
        var detector = new InstallModeDetector(assemblyPathProvider: () => globalToolPath);

        // Act
        var mode = detector.Detect();

        // Assert
        Assert.Equal(InstallMode.Global, mode);
    }

    [Fact]
    public void Detect_WhenAssemblyPathIsUnderNuGetPackagesCache_ReturnsManifest()
    {
        // Arrange
        var manifestToolPath = @"C:\Users\someuser\.nuget\packages\edict.claudeskills\0.1.0-preview.1\tools\net10.0\any\edict-skills.dll";
        var detector = new InstallModeDetector(assemblyPathProvider: () => manifestToolPath);

        // Act
        var mode = detector.Detect();

        // Assert
        Assert.Equal(InstallMode.Manifest, mode);
    }

    [Fact]
    public void Detect_WhenGlobalPathHasUpperCaseDotnetSegment_StillReturnsGlobal()
    {
        // Arrange
        var upperCasedGlobalPath = @"C:\Users\SOMEUSER\.DOTNET\Tools\.Store\edict.claudeskills\0.1.0-preview.1\edict.claudeskills\0.1.0-preview.1\tools\net10.0\any\edict-skills.dll";
        var detector = new InstallModeDetector(assemblyPathProvider: () => upperCasedGlobalPath);

        // Act
        var mode = detector.Detect();

        // Assert
        Assert.Equal(InstallMode.Global, mode);
    }

    [Fact]
    public void Detect_WhenGlobalPathUsesForwardSlashes_StillReturnsGlobal()
    {
        // Arrange
        var posixStyleGlobalPath = "/home/someuser/.dotnet/tools/.store/edict.claudeskills/0.1.0-preview.1/edict.claudeskills/0.1.0-preview.1/tools/net10.0/any/edict-skills.dll";
        var detector = new InstallModeDetector(assemblyPathProvider: () => posixStyleGlobalPath);

        // Act
        var mode = detector.Detect();

        // Assert
        Assert.Equal(InstallMode.Global, mode);
    }

    [Fact]
    public void Detect_WhenAssemblyPathIsRelativeAndUnclassifiable_ReturnsManifestAsFallback()
    {
        // Arrange
        var relativePath = @"bin\Debug\net10.0\edict-skills.dll";
        var detector = new InstallModeDetector(assemblyPathProvider: () => relativePath);

        // Act
        var mode = detector.Detect();

        // Assert
        Assert.Equal(InstallMode.Manifest, mode);
    }

    [Fact]
    public void Detect_WhenAssemblyPathIsNullOrEmpty_ReturnsManifestAsFallback()
    {
        // Arrange
        var detectorWithNull = new InstallModeDetector(assemblyPathProvider: () => null);
        var detectorWithEmpty = new InstallModeDetector(assemblyPathProvider: () => string.Empty);

        // Act
        var fromNull = detectorWithNull.Detect();
        var fromEmpty = detectorWithEmpty.Detect();

        // Assert
        Assert.Equal(InstallMode.Manifest, fromNull);
        Assert.Equal(InstallMode.Manifest, fromEmpty);
    }
}
