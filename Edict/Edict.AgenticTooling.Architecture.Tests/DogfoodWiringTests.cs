using System.Text.Json;

using Xunit;

namespace Edict.AgenticTooling.Architecture.Tests;

public class DogfoodWiringTests
{
    static readonly string RepoRoot = ResolveRepoRoot();

    [Fact]
    public void McpJsonAtRepoRoot_WiresEdictServerOverDotnetEdictMcp()
    {
        var mcpJsonPath = Path.Combine(RepoRoot, ".mcp.json");
        Assert.True(File.Exists(mcpJsonPath), $".mcp.json not found at repo root ({mcpJsonPath}).");

        using var document = JsonDocument.Parse(File.ReadAllText(mcpJsonPath));
        var edictServer = document.RootElement
            .GetProperty("mcpServers")
            .GetProperty("edict");

        Assert.Equal("dotnet", edictServer.GetProperty("command").GetString());

        var arguments = edictServer.GetProperty("args").EnumerateArray().Select(element => element.GetString()).ToArray();
        Assert.Equal(new[] { "edict-mcp" }, arguments);
    }

    [Fact]
    public void DotnetToolsManifest_ListsBothEdictToolsAtTheSameLockstepVersion()
    {
        var manifestPath = Path.Combine(RepoRoot, ".config", "dotnet-tools.json");
        Assert.True(File.Exists(manifestPath), $".config/dotnet-tools.json not found at repo root ({manifestPath}).");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var tools = document.RootElement.GetProperty("tools");

        var mcpTool = tools.GetProperty("edict.mcp");
        var skillsTool = tools.GetProperty("edict.claudeskills");

        Assert.Equal("edict-mcp", SingleCommand(mcpTool));
        Assert.Equal("edict-skills", SingleCommand(skillsTool));

        var mcpVersion = mcpTool.GetProperty("version").GetString();
        var skillsVersion = skillsTool.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(mcpVersion), "edict.mcp version is empty.");
        Assert.Equal(mcpVersion, skillsVersion);
    }

    [Theory]
    [InlineData("edict-authoring")]
    [InlineData("edict-contracts")]
    [InlineData("edict-silo-wiring")]
    [InlineData("edict-testing")]
    [InlineData("edict-diagnostics")]
    public void ConsumerSkill_IsInstalledUnderClaudeSkills(string skillName)
    {
        var skillFilePath = Path.Combine(RepoRoot, ".claude", "skills", skillName, "SKILL.md");
        Assert.True(File.Exists(skillFilePath), $"Skill '{skillName}' is not installed at {skillFilePath}.");
    }

    static string? SingleCommand(JsonElement toolElement)
    {
        var commands = toolElement.GetProperty("commands").EnumerateArray().Select(element => element.GetString()).ToArray();
        Assert.Single(commands);
        return commands[0];
    }

    static string ResolveRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "Edict"))
                && Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root from base directory.");
    }
}
