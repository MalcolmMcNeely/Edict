using System.Text.RegularExpressions;

using Edict.ClaudeSkills;

using Xunit;

namespace Edict.AgenticTooling.Architecture.Tests;

public class SkillPrescriptionTests
{
    const int CoLocationWindow = 200;

    [Fact]
    public void EdictAuthoringSkill_CoLocatesHandlerAndRouteKeyLookupsWithBefore()
    {
        var body = LoadSkillBody("edict-authoring");

        Assert.True(
            IsCoLocatedWithBefore(body, "edict_list_handlers"),
            "edict-authoring skill must co-locate `edict_list_handlers` with the word `before` so the load-bearing prescription survives.");
        Assert.True(
            IsCoLocatedWithBefore(body, "edict_list_route_keys"),
            "edict-authoring skill must co-locate `edict_list_route_keys` with the word `before` so the load-bearing prescription survives.");
    }

    [Fact]
    public void EdictSiloWiringSkill_CoLocatesDescribeSiloWiringWithBefore()
    {
        var body = LoadSkillBody("edict-silo-wiring");

        Assert.True(
            IsCoLocatedWithBefore(body, "edict_describe_silo_wiring"),
            "edict-silo-wiring skill must co-locate `edict_describe_silo_wiring` with the word `before` so the load-bearing prescription survives.");
    }

    [Fact]
    public void EdictContractsSkill_ReferencesGlossaryTermLookup()
    {
        var body = LoadSkillBody("edict-contracts");

        Assert.Contains("edict_describe_glossary_term", body, StringComparison.Ordinal);
    }

    [Fact]
    public void EdictDiagnosticsSkill_ReferencesAdrLookupAndMcpStateProbe()
    {
        var body = LoadSkillBody("edict-diagnostics");

        Assert.Contains("edict_lookup_adr", body, StringComparison.Ordinal);
        Assert.Contains("edict_describe_mcp_state", body, StringComparison.Ordinal);
    }

    static bool IsCoLocatedWithBefore(string body, string toolName)
    {
        var pattern =
            $@"(?:\bbefore\b[\s\S]{{0,{CoLocationWindow}}}{Regex.Escape(toolName)})"
            + $@"|(?:{Regex.Escape(toolName)}[\s\S]{{0,{CoLocationWindow}}}\bbefore\b)";
        return Regex.IsMatch(body, pattern, RegexOptions.IgnoreCase);
    }

    static string LoadSkillBody(string skillName)
    {
        var assembly = typeof(SkillsInstaller).Assembly;
        var resourceName = $"Edict.ClaudeSkills.Skills.{skillName}.md";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded skill resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
