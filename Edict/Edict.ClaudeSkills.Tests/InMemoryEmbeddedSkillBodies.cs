using System.Reflection;

namespace Edict.ClaudeSkills.Tests;

static class InMemoryEmbeddedSkillBodies
{
    public static IReadOnlyList<SkillsInstaller.EmbeddedSkillBody> MutateRealBodies(string replacementSuffix)
    {
        var realAssembly = typeof(SkillsInstaller).Assembly;
        const string prefix = "Edict.ClaudeSkills.Skills.";
        var bodies = new List<SkillsInstaller.EmbeddedSkillBody>();
        foreach (var resourceName in realAssembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            using var stream = realAssembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' could not be opened.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var original = memory.ToArray();
            var suffixBytes = System.Text.Encoding.UTF8.GetBytes(replacementSuffix);
            var mutated = new byte[original.Length + suffixBytes.Length];
            Buffer.BlockCopy(original, 0, mutated, 0, original.Length);
            Buffer.BlockCopy(suffixBytes, 0, mutated, original.Length, suffixBytes.Length);
            var skillFileName = resourceName[prefix.Length..];
            var skillName = Path.GetFileNameWithoutExtension(skillFileName);
            bodies.Add(SkillsInstaller.MakeEmbeddedSkillBody(skillName, mutated));
        }
        return bodies;
    }
}
