using System.Text;

using Xunit;

namespace Edict.ClaudeSkills.Tests;

public class McpJsonInspectorTests
{
    [Fact]
    public void Inspect_WhenFileDoesNotExist_ReturnsFileMissing()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var mcpJsonPath = Path.Combine(workspaceDirectory.Path, ".mcp.json");
        var inspector = new McpJsonInspector();

        // Act
        var result = inspector.Inspect(mcpJsonPath, InstallMode.Manifest);

        // Assert
        Assert.IsType<McpJsonInspection.FileMissing>(result);
    }

    [Fact]
    public void Inspect_WhenFileHasNoMcpServersKey_ReturnsNoEdictEntry()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var mcpJsonPath = workspaceDirectory.WriteFile(".mcp.json", "{}");
        var inspector = new McpJsonInspector();

        // Act
        var result = inspector.Inspect(mcpJsonPath, InstallMode.Manifest);

        // Assert
        Assert.IsType<McpJsonInspection.NoEdictEntry>(result);
    }

    [Fact]
    public void Inspect_WhenManifestFormEntryMatchesDetectedManifestMode_ReturnsEntryMatchesMode()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        const string body = """
            {
              "mcpServers": {
                "edict": { "command": "dotnet", "args": ["edict-mcp"] }
              }
            }
            """;
        var mcpJsonPath = workspaceDirectory.WriteFile(".mcp.json", body);
        var inspector = new McpJsonInspector();

        // Act
        var result = inspector.Inspect(mcpJsonPath, InstallMode.Manifest);

        // Assert
        var matched = Assert.IsType<McpJsonInspection.EntryMatchesMode>(result);
        Assert.Equal(InstallMode.Manifest, matched.Mode);
    }

    [Fact]
    public void Inspect_WhenGlobalFormEntryMatchesDetectedGlobalMode_ReturnsEntryMatchesMode()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        const string body = """
            {
              "mcpServers": {
                "edict": { "command": "edict-mcp" }
              }
            }
            """;
        var mcpJsonPath = workspaceDirectory.WriteFile(".mcp.json", body);
        var inspector = new McpJsonInspector();

        // Act
        var result = inspector.Inspect(mcpJsonPath, InstallMode.Global);

        // Assert
        var matched = Assert.IsType<McpJsonInspection.EntryMatchesMode>(result);
        Assert.Equal(InstallMode.Global, matched.Mode);
    }

    [Fact]
    public void Inspect_WhenManifestFormEntryButGlobalDetected_ReturnsEntryMismatchesMode()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        const string body = """
            {
              "mcpServers": {
                "edict": { "command": "dotnet", "args": ["edict-mcp"] }
              }
            }
            """;
        var mcpJsonPath = workspaceDirectory.WriteFile(".mcp.json", body);
        var inspector = new McpJsonInspector();

        // Act
        var result = inspector.Inspect(mcpJsonPath, InstallMode.Global);

        // Assert
        var mismatch = Assert.IsType<McpJsonInspection.EntryMismatchesMode>(result);
        Assert.Equal(InstallMode.Global, mismatch.DetectedMode);
        Assert.Equal(InstallMode.Manifest, mismatch.CurrentForm);
    }

    [Fact]
    public void Inspect_WhenGlobalFormEntryButManifestDetected_ReturnsEntryMismatchesMode()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        const string body = """
            {
              "mcpServers": {
                "edict": { "command": "edict-mcp" }
              }
            }
            """;
        var mcpJsonPath = workspaceDirectory.WriteFile(".mcp.json", body);
        var inspector = new McpJsonInspector();

        // Act
        var result = inspector.Inspect(mcpJsonPath, InstallMode.Manifest);

        // Assert
        var mismatch = Assert.IsType<McpJsonInspection.EntryMismatchesMode>(result);
        Assert.Equal(InstallMode.Manifest, mismatch.DetectedMode);
        Assert.Equal(InstallMode.Global, mismatch.CurrentForm);
    }

    [Fact]
    public void Inspect_WhenJsonHasCommentsAndTrailingCommas_ParsesPermissivelyAndClassifies()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        const string body = """
            {
              // Linear MCP for issue tracking
              "mcpServers": {
                "linear": { "command": "linear-mcp" }, /* sentry next */
                "edict": { "command": "dotnet", "args": ["edict-mcp"] },
              }
            }
            """;
        var mcpJsonPath = workspaceDirectory.WriteFile(".mcp.json", body);
        var inspector = new McpJsonInspector();

        // Act
        var result = inspector.Inspect(mcpJsonPath, InstallMode.Manifest);

        // Assert
        Assert.IsType<McpJsonInspection.EntryMatchesMode>(result);
    }

    [Fact]
    public void Inspect_WhenFileStartsWithUtf8Bom_ParsesAndClassifies()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        const string body = """
            {
              "mcpServers": {
                "edict": { "command": "edict-mcp" }
              }
            }
            """;
        var mcpJsonPath = Path.Combine(workspaceDirectory.Path, ".mcp.json");
        var encodingWithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        File.WriteAllText(mcpJsonPath, body, encodingWithBom);
        var inspector = new McpJsonInspector();

        // Act
        var result = inspector.Inspect(mcpJsonPath, InstallMode.Global);

        // Assert
        Assert.IsType<McpJsonInspection.EntryMatchesMode>(result);
    }
}
