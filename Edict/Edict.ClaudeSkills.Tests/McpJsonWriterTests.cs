using Xunit;

namespace Edict.ClaudeSkills.Tests;

public class McpJsonWriterTests
{
    [Fact]
    public void Write_WhenManifestMode_WritesCanonicalManifestFormJson()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var targetPath = Path.Combine(workspaceDirectory.Path, ".mcp.json");
        var writer = new McpJsonWriter();

        // Act
        writer.Write(targetPath, InstallMode.Manifest);

        // Assert
        var written = File.ReadAllText(targetPath);
        const string expected = """
            {
              "mcpServers": {
                "edict": {
                  "command": "dotnet",
                  "args": [
                    "edict-mcp"
                  ]
                }
              }
            }
            """;
        Assert.Equal(expected, written);
    }

    [Fact]
    public void Write_WhenGlobalMode_WritesCanonicalGlobalFormJson()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var targetPath = Path.Combine(workspaceDirectory.Path, ".mcp.json");
        var writer = new McpJsonWriter();

        // Act
        writer.Write(targetPath, InstallMode.Global);

        // Assert
        var written = File.ReadAllText(targetPath);
        const string expected = """
            {
              "mcpServers": {
                "edict": {
                  "command": "edict-mcp"
                }
              }
            }
            """;
        Assert.Equal(expected, written);
    }

    [Fact]
    public void Write_PersistsFileAsUtf8WithoutBom()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var targetPath = Path.Combine(workspaceDirectory.Path, ".mcp.json");
        var writer = new McpJsonWriter();

        // Act
        writer.Write(targetPath, InstallMode.Manifest);

        // Assert
        var rawBytes = File.ReadAllBytes(targetPath);
        Assert.False(
            rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF,
            "File must not start with a UTF-8 BOM.");
        Assert.Equal((byte)'{', rawBytes[0]);
    }

    [Fact]
    public void Write_UsesTwoSpaceIndentation()
    {
        // Arrange
        using var workspaceDirectory = new TempTargetDirectory();
        var targetPath = Path.Combine(workspaceDirectory.Path, ".mcp.json");
        var writer = new McpJsonWriter();

        // Act
        writer.Write(targetPath, InstallMode.Manifest);

        // Assert
        var lines = File.ReadAllText(targetPath).Split('\n');
        var mcpServersLine = lines.Single(line => line.Contains("\"mcpServers\""));
        Assert.StartsWith("  \"mcpServers\"", mcpServersLine);
        var commandLine = lines.Single(line => line.Contains("\"command\""));
        Assert.StartsWith("      \"command\"", commandLine);
    }
}
