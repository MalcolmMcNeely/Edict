using System.Reflection;

using Edict.ClaudeSkills;
using Edict.Mcp;

using Xunit;

namespace Edict.AgenticTooling.Architecture.Tests;

public class McpJsonModulePlacementTests
{
    static readonly Assembly ClaudeSkillsAssembly = typeof(SkillsInstaller).Assembly;
    static readonly Assembly McpAssembly = typeof(EdictMcpServer).Assembly;

    [Theory]
    [InlineData(typeof(InstallModeDetector))]
    [InlineData(typeof(McpJsonInspector))]
    [InlineData(typeof(McpJsonWriter))]
    public void McpJsonModule_LivesInClaudeSkillsAssembly(Type moduleType)
    {
        Assert.Same(ClaudeSkillsAssembly, moduleType.Assembly);
    }

    [Fact]
    public void EdictMcpAssembly_DoesNotDeclareMcpJsonModules()
    {
        var leakedTypeNames = McpAssembly
            .GetTypes()
            .Select(type => type.Name)
            .Where(name => name is "InstallModeDetector" or "McpJsonInspector" or "McpJsonWriter")
            .ToList();

        Assert.Empty(leakedTypeNames);
    }
}
