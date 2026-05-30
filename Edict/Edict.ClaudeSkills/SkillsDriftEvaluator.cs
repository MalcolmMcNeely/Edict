namespace Edict.ClaudeSkills;

public static class SkillsDriftEvaluator
{
    public static IReadOnlyDictionary<string, SkillDriftDecision> Evaluate(
        SkillsManifest? currentManifest,
        IReadOnlyCollection<EmbeddedSkillDescriptor> embeddedSkills,
        IReadOnlyCollection<OnDiskSkillDescriptor> onDiskSkills,
        bool force)
    {
        var onDiskByName = onDiskSkills.ToDictionary(skill => skill.Name, skill => skill.ContentHash, StringComparer.Ordinal);
        var manifestSkills = currentManifest?.Skills;

        var decisions = new Dictionary<string, SkillDriftDecision>(StringComparer.Ordinal);
        foreach (var embedded in embeddedSkills)
        {
            decisions[embedded.Name] = DecideFor(embedded.Name, onDiskByName, manifestSkills, force);
        }
        return decisions;
    }

    static SkillDriftDecision DecideFor(
        string skillName,
        IReadOnlyDictionary<string, string> onDiskByName,
        IReadOnlyDictionary<string, string>? manifestSkills,
        bool force)
    {
        var diskHash = onDiskByName.TryGetValue(skillName, out var hash) ? hash : null;
        var manifestHash = manifestSkills is not null && manifestSkills.TryGetValue(skillName, out var manifestEntry)
            ? manifestEntry
            : null;

        if (diskHash is null)
        {
            return SkillDriftDecision.Create;
        }

        if (manifestHash is null)
        {
            return force ? SkillDriftDecision.Refresh : SkillDriftDecision.SkipDrifted;
        }

        if (string.Equals(diskHash, manifestHash, StringComparison.Ordinal))
        {
            return SkillDriftDecision.Refresh;
        }

        return force ? SkillDriftDecision.Refresh : SkillDriftDecision.SkipDrifted;
    }
}
