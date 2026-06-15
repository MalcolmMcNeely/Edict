using Edict.ClaudeSkills;
using Edict.Mcp.SiloWiring;

using Xunit;

namespace Edict.AgenticTooling.Architecture.Tests;

public class SiloWiringAuditCatalogueInterlockTests
{
    const string AuditOnSwitchName = "WithAudit";
    const string SiloWiringSkillName = "edict-silo-wiring";

    [Fact]
    public void SiloWiringCatalogue_CarriesTheAuditOnSwitch()
    {
        var catalogueNames = SiloWiringScanner.KnownExtensions.Select(entry => entry.ExtensionName);

        Assert.Contains(AuditOnSwitchName, catalogueNames);
    }

    [Fact]
    public void SiloWiringSkill_NamesTheAuditOnSwitchTheCatalogueReports()
    {
        var skillBody = LoadShippedSkillBody(SiloWiringSkillName);

        Assert.Contains(AuditOnSwitchName, skillBody, StringComparison.Ordinal);
    }

    static string LoadShippedSkillBody(string skillName)
    {
        var assembly = typeof(SkillsInstaller).Assembly;
        var resourceName = $"Edict.ClaudeSkills.Skills.{skillName}.md";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded skill resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
