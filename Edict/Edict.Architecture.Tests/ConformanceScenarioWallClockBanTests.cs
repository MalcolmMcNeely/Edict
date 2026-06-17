using Xunit;

namespace Edict.Architecture.Tests;

// Capstone drift guard for the determinism discipline: no *Scenarios.cs file in the
// axis-conformance batteries may sleep on the wall clock. A raw Task.Delay reintroduces
// the exact flake the deterministic seams exist to avoid, so this turns red and names
// the file and line the moment one is added — with no allowlist, every scenario file is
// held to the invariant. The benign poll-loop pacing Task.Delay inside *Waiters helpers
// is out of scope by construction: the scan is scoped to *Scenarios.cs only. Pairs with
// the binding-completeness guard, which enforces provider symmetry rather than coverage.
public class ConformanceScenarioWallClockBanTests
{
    [Fact]
    public void NoConformanceScenarioFile_SleepsOnTheWallClock()
    {
        var conformanceRoot = Path.Combine(GetSolutionRoot(), "Edict.Tests.Conformance");
        Assert.True(Directory.Exists(conformanceRoot), $"Conformance project directory missing: {conformanceRoot}");

        var scenarioFiles = Directory
            .EnumerateFiles(conformanceRoot, "*Scenarios.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToList();

        Assert.True(scenarioFiles.Count > 0, $"No *Scenarios.cs files discovered under {conformanceRoot}.");

        var violations = scenarioFiles
            .SelectMany(file => ConformanceScenarioWallClockBan.FindWallClockSleeps(Path.GetFileName(file), File.ReadAllText(file)))
            .ToList();

        Assert.True(violations.Count == 0, "Wall-clock sleeps in conformance scenarios:\n" + string.Join("\n", violations));
    }

    static string GetSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !directory.EnumerateFiles("*.slnx").Any())
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
