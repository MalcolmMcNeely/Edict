using System.Collections.Immutable;
using System.Text.RegularExpressions;

using Edict.Analyzers.Interceptors;
using Edict.ClaudeSkills;
using Edict.Mcp.Docs;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

using Xunit;

namespace Edict.AgenticTooling.Architecture.Tests;

// Tenancy widened the agentic surface: the skills now cite the EDICT024 origin-send
// analyzer and lean on the tenancy glossary terms. These facts fail the moment a cited
// id is renamed off its analyzer or a referenced glossary term is removed from CONTEXT.md,
// so a skill can never ship a dangling pointer an agent would follow into nothing.
public class TenancyAgenticToolingInterlockTests
{
    static readonly Regex DiagnosticIdPattern = new(@"\bEDICT\d{3}\b");

    [Fact]
    public void EverySkillCitedEdictDiagnosticId_ResolvesToARealAnalyzerDescriptor()
    {
        var declaredIds = LoadDeclaredDiagnosticIds();
        var citedIds = LoadShippedSkillBodies()
            .SelectMany(skill => DiagnosticIdPattern.Matches(skill.Body).Select(match => match.Value))
            .ToHashSet(StringComparer.Ordinal);

        var dangling = citedIds.Where(id => !declaredIds.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();

        Assert.Empty(dangling);
    }

    [Fact]
    public void TheTenantOriginSendAnalyzerId_IsCitedByASkill()
    {
        var citedIds = LoadShippedSkillBodies()
            .SelectMany(skill => DiagnosticIdPattern.Matches(skill.Body).Select(match => match.Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(OriginSendTenantAnalyzer.DiagnosticId, citedIds);
    }

    [Theory]
    [InlineData("Tenant")]
    [InlineData("Tenant-Scoped Aggregate")]
    [InlineData("Public Aggregate")]
    [InlineData("Company Wall")]
    [InlineData("Establishing Crossing")]
    [InlineData("Ambient-Scoped Read")]
    [InlineData("Isolation Call Filter")]
    [InlineData("Routed Key")]
    public void EverySkillReferencedTenancyGlossaryTerm_ResolvesInContextMarkdown(string term)
    {
        var contextMarkdown = File.ReadAllText(Path.Combine(AgenticToolingTestPaths.RepoRoot, "CONTEXT.md"));
        var docs = new DocsLookup(contextMarkdown, []);

        Assert.NotNull(docs.LookupGlossaryTerm(term));
    }

    static IReadOnlySet<string> LoadDeclaredDiagnosticIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in typeof(OriginSendTenantAnalyzer).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            {
                continue;
            }
            var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
            foreach (var descriptor in (ImmutableArray<DiagnosticDescriptor>)analyzer.SupportedDiagnostics)
            {
                ids.Add(descriptor.Id);
            }
        }
        return ids;
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
            bodies.Add(new SkillBody(Path.GetFileNameWithoutExtension(resourceName[prefix.Length..]), reader.ReadToEnd()));
        }
        return bodies;
    }

    sealed record SkillBody(string Name, string Body);
}
