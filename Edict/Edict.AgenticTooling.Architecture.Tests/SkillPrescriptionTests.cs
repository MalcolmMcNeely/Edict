using System.Text.RegularExpressions;

using Edict.ClaudeSkills;
using Edict.Mcp.Handlers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
    public void EdictAuthoringSkill_PinsBothHalvesOfThePrincipalAttributionContrast()
    {
        var body = LoadSkillBody("edict-authoring");

        Assert.True(
            AreCoLocated(body, "actor-less", "system"),
            "edict-authoring must scope a constant `system` principal to an actor-less origin so the correct-attribution half of the contrast survives.");
        Assert.True(
            AreCoLocated(body, "user-initiated", "severe"),
            "edict-authoring must warn that a constant/system principal on a user-initiated send is the severe failure so the misattribution half of the contrast survives.");
    }

    [Fact]
    public void EdictAuthoringSkill_DescribesHowToAuthorACommandValidator()
    {
        var body = LoadSkillBody("edict-authoring");

        Assert.Contains("EdictCommandValidator", body, StringComparison.Ordinal);
        Assert.Contains("AbstractValidator", body, StringComparison.Ordinal);
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
    public void EdictSiloWiringSkill_CoLocatesCheckConfigurationWithBefore()
    {
        var body = LoadSkillBody("edict-silo-wiring");

        Assert.True(
            IsCoLocatedWithBefore(body, "edict_check_configuration"),
            "edict-silo-wiring skill must co-locate `edict_check_configuration` with the word `before` so the after-wiring prescription survives.");
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

    [Fact]
    public void EdictDiagnosticsSkill_ReferencesCheckConfigurationFromTheBootFailurePath()
    {
        var body = LoadSkillBody("edict-diagnostics");

        Assert.Contains("edict_check_configuration", body, StringComparison.Ordinal);
    }

    [Fact]
    public void EdictDiagnosticsSkill_ExplainsReminderRegistrationExhaustion()
    {
        var body = LoadSkillBody("edict-diagnostics");

        Assert.Contains("EdictReminderRegistrationException", body, StringComparison.Ordinal);
        Assert.True(
            AreCoLocated(body, "EdictReminderRegistrationException", "reminder service"),
            "edict-diagnostics must tie EdictReminderRegistrationException to reminder-service unavailability / silo health so the diagnosis survives.");
        Assert.True(
            AreCoLocated(body, "EdictReminderRegistrationException", "cold-start"),
            "edict-diagnostics must explain that EdictReminderRegistrationException means the cold-start retry window was already exhausted, so a consumer does not wrap it in their own retry.");
    }

    [Fact]
    public void EdictDiagnosticsSkill_TeachesTheConversationIdQueryRecipe()
    {
        var body = LoadSkillBody("edict-diagnostics");

        Assert.Contains("messaging.message.conversation_id", body, StringComparison.Ordinal);
        Assert.True(
            AreCoLocated(body, "messaging.message.conversation_id", "filter"),
            "edict-diagnostics must co-locate the standard `messaging.message.conversation_id` attribute with the `filter` query verb so the filter-then-follow-links recipe survives.");
    }

    [Theory]
    [MemberData(nameof(HandlerRoleNames))]
    public void EdictMcp_HandlerRole_EveryEnumValueIsCoveredByScanner(string roleName)
    {
        var role = Enum.Parse<HandlerRole>(roleName);
        var consumerSource = HandlerRoleFixtures.ConsumerSourceFor(role);
        var compilation = CreateCompilation("ConsumerLibrary", HandlerRoleFixtures.EdictBasesSource, consumerSource);
        var scanner = new HandlerScanner();

        var inventory = scanner.Scan([compilation], solutionDirectory: null);

        var entry = Assert.Single(inventory.Handlers);
        Assert.Equal(role, entry.Role);
    }

    public static TheoryData<string> HandlerRoleNames()
    {
        var data = new TheoryData<string>();
        foreach (var role in Enum.GetNames<HandlerRole>())
        {
            data.Add(role);
        }
        return data;
    }

    static CSharpCompilation CreateCompilation(string assemblyName, params string[] sources)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Cast<MetadataReference>()
            .ToList();
        var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source));
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    static bool AreCoLocated(string body, string first, string second)
    {
        var pattern =
            $@"(?:{Regex.Escape(first)}[\s\S]{{0,{CoLocationWindow}}}{Regex.Escape(second)})"
            + $@"|(?:{Regex.Escape(second)}[\s\S]{{0,{CoLocationWindow}}}{Regex.Escape(first)})";
        return Regex.IsMatch(body, pattern, RegexOptions.IgnoreCase);
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
