using System.Reflection;

using Edict.Analyzers.Partial;
using Edict.Generators;

using Xunit;

namespace Edict.Analyzers.Tests;

/// <summary>
/// Guards against the <c>EdictWellKnownNames.cs</c> file diverging between
/// <c>Edict.Generators</c> and <c>Edict.Analyzers</c>.
/// The file is compile-linked (not duplicated), so the test mainly protects
/// against accidental inline copies or accidental renames of the shared source.
/// </summary>
public class EdictWellKnownNamesParityTests
{
    static readonly Assembly GeneratorsAssembly =
        typeof(EdictGenerator).Assembly;

    static readonly Assembly AnalyzersAssembly =
        typeof(GrainMustBePartialAnalyzer).Assembly;

    [Fact]
    public void EdictWellKnownNames_ShouldExistInBothAssemblies()
    {
        var generatorsType = GeneratorsAssembly.GetType("Edict.Generators.EdictWellKnownNames");
        var analyzersType = AnalyzersAssembly.GetType("Edict.Generators.EdictWellKnownNames");

        Assert.NotNull(generatorsType);
        Assert.NotNull(analyzersType);
    }

    [Fact]
    public void EdictWellKnownNames_ShouldHaveIdenticalConstantsInGeneratorsAndAnalyzers()
    {
        var generatorsType = GeneratorsAssembly.GetType("Edict.Generators.EdictWellKnownNames")!;
        var analyzersType = AnalyzersAssembly.GetType("Edict.Generators.EdictWellKnownNames")!;

        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        var generatorsConsts = generatorsType.GetFields(flags)
            .Where(f => f.IsLiteral)
            .OrderBy(f => f.Name)
            .Select(f => $"{f.Name}={f.GetRawConstantValue()}")
            .ToList();

        var analyzersConsts = analyzersType.GetFields(flags)
            .Where(f => f.IsLiteral)
            .OrderBy(f => f.Name)
            .Select(f => $"{f.Name}={f.GetRawConstantValue()}")
            .ToList();

        Assert.Equal(generatorsConsts, analyzersConsts);
    }
}
