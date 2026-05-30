using System.Text.Json;

namespace Edict.ClaudeSkills;

public sealed record SkillsManifest(
    string InstalledVersion,
    IReadOnlyDictionary<string, string> Skills)
{
    public const string ManifestPath = ".claude/skills/.edict-skills-manifest.json";

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
