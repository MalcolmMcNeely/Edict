using System.Text.Json;

using VerifyXunit;

using Xunit;

namespace Edict.ClaudeSkills.Tests;

public class SkillsManifestTests
{
    [Fact]
    public Task WireShape_RoundTripThroughSystemTextJson_MatchesSnapshot()
    {
        // Arrange
        var manifest = new SkillsManifest(
            InstalledVersion: "0.3.0",
            Skills: new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["edict-authoring"] = "sha256:0000000000000000000000000000000000000000000000000000000000000001",
                ["edict-contracts"] = "sha256:0000000000000000000000000000000000000000000000000000000000000002",
                ["edict-diagnostics"] = "sha256:0000000000000000000000000000000000000000000000000000000000000003",
                ["edict-silo-wiring"] = "sha256:0000000000000000000000000000000000000000000000000000000000000004",
                ["edict-testing"] = "sha256:0000000000000000000000000000000000000000000000000000000000000005",
            });

        // Act
        var serialised = JsonSerializer.Serialize(manifest, SkillsManifest.SerializerOptions);
        var roundTripped = JsonSerializer.Deserialize<SkillsManifest>(serialised, SkillsManifest.SerializerOptions);

        // Assert
        return Verifier.Verify(new
        {
            Serialised = serialised,
            RoundTrippedInstalledVersion = roundTripped!.InstalledVersion,
            RoundTrippedSkills = roundTripped.Skills,
        });
    }

    [Fact]
    public void ManifestPath_IsClaudeSkillsDotEdictSkillsManifestJson()
    {
        Assert.Equal(".claude/skills/.edict-skills-manifest.json", SkillsManifest.ManifestPath);
    }
}
