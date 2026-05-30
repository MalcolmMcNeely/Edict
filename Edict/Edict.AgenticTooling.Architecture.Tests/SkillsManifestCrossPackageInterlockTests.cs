using System.Reflection;

using Xunit;

using ClaudeSkillsManifest = Edict.ClaudeSkills.SkillsManifest;
using McpSkillsManifest = Edict.Mcp.Versioning.SkillsManifest;

namespace Edict.AgenticTooling.Architecture.Tests;

public class SkillsManifestCrossPackageInterlockTests
{
    [Fact]
    public void ManifestPath_HasSameValueInBothPackages()
    {
        Assert.Equal(ClaudeSkillsManifest.ManifestPath, McpSkillsManifest.ManifestPath);
    }

    [Fact]
    public void SkillsManifestRecord_HasSameShapeInBothPackages()
    {
        var claudeSkillsShape = DescribeRecordShape(typeof(ClaudeSkillsManifest));
        var mcpShape = DescribeRecordShape(typeof(McpSkillsManifest));

        if (!claudeSkillsShape.SequenceEqual(mcpShape, StringComparer.Ordinal))
        {
            Assert.Fail(
                $"SkillsManifest record shape differs between packages.{Environment.NewLine}" +
                $"Edict.ClaudeSkills.SkillsManifest:{Environment.NewLine}" +
                $"  {string.Join(Environment.NewLine + "  ", claudeSkillsShape)}{Environment.NewLine}" +
                $"Edict.Mcp.Versioning.SkillsManifest:{Environment.NewLine}" +
                $"  {string.Join(Environment.NewLine + "  ", mcpShape)}");
        }
    }

    static IReadOnlyList<string> DescribeRecordShape(Type recordType)
    {
        return recordType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != "EqualityContract")
            .Select(property => $"{property.Name}: {FormatTypeName(property.PropertyType)}")
            .ToList();
    }

    static string FormatTypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }
        var genericArguments = string.Join(", ", type.GetGenericArguments().Select(FormatTypeName));
        var unqualifiedName = type.Name[..type.Name.IndexOf('`')];
        return $"{type.Namespace}.{unqualifiedName}<{genericArguments}>";
    }
}
