using System.Reflection;

using Edict.Analyzers.Partial;
using Edict.Generators;
using Edict.Mcp;

using Xunit;

namespace Edict.Analyzers.Tests;

/// <summary>
/// Guards against the <c>EdictWellKnownNames.cs</c> file diverging between
/// <c>Edict.Generators</c>, <c>Edict.Analyzers</c>, and <c>Edict.Mcp</c>.
/// The file is compile-linked (not duplicated), so the test mainly protects
/// against accidental inline copies or accidental renames of the shared source.
/// </summary>
public class EdictWellKnownNamesParityTests
{
    static readonly Assembly GeneratorsAssembly =
        typeof(EdictGenerator).Assembly;

    static readonly Assembly AnalyzersAssembly =
        typeof(GrainMustBePartialAnalyzer).Assembly;

    static readonly Assembly McpAssembly =
        typeof(EdictMcpServer).Assembly;

    [Fact]
    public void EdictWellKnownNames_ShouldExistInAllConsumingAssemblies()
    {
        Assert.NotNull(GeneratorsAssembly.GetType("Edict.Generators.EdictWellKnownNames"));
        Assert.NotNull(AnalyzersAssembly.GetType("Edict.Generators.EdictWellKnownNames"));
        Assert.NotNull(McpAssembly.GetType("Edict.Generators.EdictWellKnownNames"));
    }

    [Fact]
    public void EdictWellKnownNames_ShouldHaveIdenticalConstantsAcrossAllConsumingAssemblies()
    {
        var generatorsConsts = ReadConstants(GeneratorsAssembly);
        var analyzersConsts = ReadConstants(AnalyzersAssembly);
        var mcpConsts = ReadConstants(McpAssembly);

        Assert.Equal(generatorsConsts, analyzersConsts);
        Assert.Equal(generatorsConsts, mcpConsts);
    }

    static List<string> ReadConstants(Assembly assembly)
    {
        var type = assembly.GetType("Edict.Generators.EdictWellKnownNames")!;

        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        return type.GetFields(flags)
            .Where(field => field.IsLiteral)
            .OrderBy(field => field.Name)
            .Select(field => $"{field.Name}={field.GetRawConstantValue()}")
            .ToList();
    }
}
