using Xunit;

namespace Edict.ClaudeSkills.Tests;

public class McpInstallerTests
{
    static readonly string ManifestDetectionPath = @"C:\not-a-tools-path\bin\Debug\net10.0\edict-skills.dll";
    static readonly string GlobalDetectionPath = @"C:\Users\someuser\.dotnet\tools\.store\edict.claudeskills\0.1.0-preview.1\edict.claudeskills\0.1.0-preview.1\tools\net10.0\any\edict-skills.dll";

    [Fact]
    public void Install_WhenMcpJsonAbsent_CreatesFileMatchingDetectedManifestMode()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var installer = NewInstaller(workspaceDirectory.Path, ManifestDetectionPath);

        // Act
        var report = installer.Install();

        // Assert
        var expectedPath = Path.Combine(workspaceDirectory.Path, ".mcp.json");
        Assert.Equal(expectedPath, report.McpJsonPath);
        Assert.Equal(InstallMode.Manifest, report.DetectedMode);
        Assert.Equal(McpInstallAction.CreatedFile, report.Action);
        Assert.Null(report.ExistingForm);
        Assert.True(File.Exists(expectedPath));
        Assert.Contains("\"command\": \"dotnet\"", File.ReadAllText(expectedPath));
    }

    [Fact]
    public void Install_WhenMcpJsonAbsentAndGlobalDetected_CreatesGlobalFormFile()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var installer = NewInstaller(workspaceDirectory.Path, GlobalDetectionPath);

        // Act
        var report = installer.Install();

        // Assert
        Assert.Equal(InstallMode.Global, report.DetectedMode);
        Assert.Equal(McpInstallAction.CreatedFile, report.Action);
        var written = File.ReadAllText(report.McpJsonPath);
        Assert.Contains("\"command\": \"edict-mcp\"", written);
        Assert.DoesNotContain("\"args\"", written);
    }

    [Fact]
    public void Install_WhenMcpJsonExistsWithNoEdictEntry_LeavesFileUntouchedAndReportsInstructionsToAdd()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        const string original = """
            {
              "mcpServers": {
                "linear": { "command": "linear-mcp" }
              }
            }
            """;
        var mcpJsonPath = workspaceDirectory.WriteFile(".mcp.json", original);
        var installer = NewInstaller(workspaceDirectory.Path, ManifestDetectionPath);

        // Act
        var report = installer.Install();

        // Assert
        Assert.Equal(McpInstallAction.InstructionsToAdd, report.Action);
        Assert.Null(report.ExistingForm);
        Assert.Equal(original, File.ReadAllText(mcpJsonPath));
    }

    [Fact]
    public void Install_WhenMcpJsonExistsWithMatchingEntry_LeavesFileUntouchedAndReportsAlreadyWired()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        const string original = """
            {
              "mcpServers": {
                "edict": { "command": "dotnet", "args": ["edict-mcp"] }
              }
            }
            """;
        var mcpJsonPath = workspaceDirectory.WriteFile(".mcp.json", original);
        var installer = NewInstaller(workspaceDirectory.Path, ManifestDetectionPath);

        // Act
        var report = installer.Install();

        // Assert
        Assert.Equal(McpInstallAction.AlreadyWired, report.Action);
        Assert.Equal(InstallMode.Manifest, report.ExistingForm);
        Assert.Equal(original, File.ReadAllText(mcpJsonPath));
    }

    [Fact]
    public void Install_WhenMcpJsonExistsWithMismatchedEntry_LeavesFileUntouchedAndReportsInstructionsToUpdate()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        const string original = """
            {
              "mcpServers": {
                "edict": { "command": "dotnet", "args": ["edict-mcp"] }
              }
            }
            """;
        var mcpJsonPath = workspaceDirectory.WriteFile(".mcp.json", original);
        var installer = NewInstaller(workspaceDirectory.Path, GlobalDetectionPath);

        // Act
        var report = installer.Install();

        // Assert
        Assert.Equal(McpInstallAction.InstructionsToUpdate, report.Action);
        Assert.Equal(InstallMode.Global, report.DetectedMode);
        Assert.Equal(InstallMode.Manifest, report.ExistingForm);
        Assert.Equal(original, File.ReadAllText(mcpJsonPath));
    }

    static McpInstaller NewInstaller(string workspacePath, string detectionAssemblyPath)
    {
        return new McpInstaller(
            installModeDetector: new InstallModeDetector(assemblyPathProvider: () => detectionAssemblyPath),
            mcpJsonInspector: new McpJsonInspector(),
            mcpJsonWriter: new McpJsonWriter(),
            currentDirectoryProvider: () => workspacePath);
    }
}
