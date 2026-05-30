using System.Reflection;

using Xunit;

namespace Edict.ClaudeSkills.Tests;

public class SkillBodyTests
{
    static readonly Assembly ClaudeSkillsAssembly = typeof(SkillsInstaller).Assembly;

    [Theory]
    [InlineData("edict-authoring", "edict_list_handlers")]
    [InlineData("edict-authoring", "edict_list_route_keys")]
    [InlineData("edict-authoring", "edict_describe_glossary_term")]
    [InlineData("edict-contracts", "edict_lookup_adr")]
    [InlineData("edict-silo-wiring", "edict_describe_silo_wiring")]
    [InlineData("edict-diagnostics", "edict_lookup_adr")]
    [InlineData("edict-diagnostics", "edict_describe_mcp_state")]
    public void SkillBody_NamesItsMcpToolTrigger(string skillName, string toolName)
    {
        var body = ReadEmbeddedSkillBody(skillName);

        Assert.Contains(toolName, body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("edict-authoring")]
    [InlineData("edict-contracts")]
    [InlineData("edict-silo-wiring")]
    [InlineData("edict-testing")]
    [InlineData("edict-diagnostics")]
    public void SkillDescription_IsScopedToConsumerApp(string skillName)
    {
        var body = ReadEmbeddedSkillBody(skillName);

        Assert.Contains("consumer app built on Edict", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("EdictTestApp")]
    [InlineData("WithConsumer")]
    [InlineData("Replace")]
    [InlineData("GetSagaProgress")]
    [InlineData("GetProjectionRow")]
    [InlineData("Timeline")]
    [InlineData("Drain")]
    [InlineData("AdvanceClock")]
    [InlineData("IEdictSender")]
    [InlineData("chaos")]
    public void EdictTestingSkill_CoversTheEdictTestingSurface(string surfaceTerm)
    {
        var body = ReadEmbeddedSkillBody("edict-testing");

        Assert.Contains(surfaceTerm, body, StringComparison.OrdinalIgnoreCase);
    }

    static string ReadEmbeddedSkillBody(string skillName)
    {
        var resourceName = $"Edict.ClaudeSkills.Skills.{skillName}.md";
        using var resourceStream = ClaudeSkillsAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded skill resource '{resourceName}' not found.");
        using var reader = new StreamReader(resourceStream);
        return reader.ReadToEnd();
    }
}
