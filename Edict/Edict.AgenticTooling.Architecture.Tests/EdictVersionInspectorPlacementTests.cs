using System.Reflection;

using Edict.ClaudeSkills;
using Edict.Mcp;
using Edict.Mcp.Versioning;

using Xunit;

namespace Edict.AgenticTooling.Architecture.Tests;

public class EdictVersionInspectorPlacementTests
{
    static readonly Assembly McpAssembly = typeof(EdictMcpServer).Assembly;
    static readonly Assembly ClaudeSkillsAssembly = typeof(SkillsInstaller).Assembly;

    [Fact]
    public void EdictVersionInspector_LivesInMcpAssembly()
    {
        Assert.Same(McpAssembly, typeof(EdictVersionInspector).Assembly);
    }

    [Fact]
    public void ClaudeSkillsAssembly_DoesNotDeclareEdictVersionInspector()
    {
        var leakedTypeNames = ClaudeSkillsAssembly
            .GetTypes()
            .Select(type => type.Name)
            .Where(name => name is "EdictVersionInspector" or "EdictVersionReport" or "EdictVersionReference")
            .ToList();

        Assert.Empty(leakedTypeNames);
    }
}
