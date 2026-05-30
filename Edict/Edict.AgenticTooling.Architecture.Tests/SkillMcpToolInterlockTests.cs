using System.Text.RegularExpressions;

using Edict.ClaudeSkills;
using Edict.Mcp.Tools;
using Edict.Mcp.Workspaces;

using Xunit;

namespace Edict.AgenticTooling.Architecture.Tests;

public class SkillMcpToolInterlockTests
{
    static readonly Regex McpToolReferencePattern = new(@"\bedict_[a-z]+(?:_[a-z]+)*\b");

    [Fact]
    public void EveryRegisteredMcpTool_HasAtLeastOneSkillCaller()
    {
        var registeredToolNames = LoadRegisteredToolNames();
        var skillBodies = LoadShippedSkillBodies();

        var orphans = registeredToolNames
            .Where(toolName => skillBodies.All(skill => !skill.Body.Contains(toolName, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void EverySkillMcpToolReference_ResolvesToRegisteredTool()
    {
        var registeredToolNames = LoadRegisteredToolNames().ToHashSet(StringComparer.Ordinal);
        var skillBodies = LoadShippedSkillBodies();

        var danglingReferences = new List<string>();
        foreach (var skill in skillBodies)
        {
            foreach (Match match in McpToolReferencePattern.Matches(skill.Body))
            {
                if (!registeredToolNames.Contains(match.Value))
                {
                    danglingReferences.Add($"{skill.Name}: {match.Value}");
                }
            }
        }

        Assert.Empty(danglingReferences);
    }

    static IReadOnlyList<string> LoadRegisteredToolNames()
    {
        var workspaceProvider = new MSBuildWorkspaceProvider(
            solutionOverride: null,
            currentDirectoryProvider: () => Path.GetTempPath());
        var registry = new McpToolRegistry(workspaceProvider);
        return registry.Tools.Select(tool => tool.Name).ToList();
    }

    static IReadOnlyList<SkillBody> LoadShippedSkillBodies()
    {
        var assembly = typeof(SkillsInstaller).Assembly;
        const string prefix = "Edict.ClaudeSkills.Skills.";
        var bodies = new List<SkillBody>();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded skill resource '{resourceName}' could not be opened.");
            using var reader = new StreamReader(stream);
            var body = reader.ReadToEnd();
            var skillName = Path.GetFileNameWithoutExtension(resourceName[prefix.Length..]);
            bodies.Add(new SkillBody(skillName, body));
        }
        return bodies;
    }

    sealed record SkillBody(string Name, string Body);
}
