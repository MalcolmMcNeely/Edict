using System.Reflection;

using Edict.ClaudeSkills;

using Xunit;

namespace Edict.AgenticTooling.Architecture.Tests;

public class ClaudeSkillsFrameworkIsolationTests
{
    static readonly Assembly ClaudeSkillsAssembly = typeof(SkillsInstaller).Assembly;

    [Fact]
    public void EdictClaudeSkills_HasNoEdictFrameworkReferences()
    {
        var violations = ReferencedAssemblyNames()
            .Where(name => name.StartsWith("Edict.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(violations);
    }

    static IEnumerable<string> ReferencedAssemblyNames()
    {
        return ClaudeSkillsAssembly
            .GetReferencedAssemblies()
            .Select(referenced => referenced.Name)
            .Where(name => name is not null)!;
    }
}
