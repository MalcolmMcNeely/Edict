namespace Edict.Architecture.Tests;

// Pure wall-clock-ban matcher for the axis-conformance scenarios: given a file's
// name and source text, it reports every line that sleeps on the wall clock via
// Task.Delay. The scope check is part of the matcher, not the caller — only files
// named *Scenarios.cs are inspected, so the benign poll-loop pacing inside *Waiters
// helpers is exempt by construction. There is no allowlist: every scenario file is
// held to the same invariant.
static class ConformanceScenarioWallClockBan
{
    public static IReadOnlyCollection<string> FindWallClockSleeps(string fileName, string sourceText)
    {
        var violations = new List<string>();
        if (!fileName.EndsWith("Scenarios.cs", StringComparison.Ordinal))
        {
            return violations;
        }

        var lines = sourceText.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lines[lineIndex].Contains("Task.Delay", StringComparison.Ordinal))
            {
                violations.Add($"{fileName}:{lineIndex + 1}: {lines[lineIndex].Trim()}");
            }
        }
        return violations;
    }
}
