using System.Collections.Immutable;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Edict.Core.Tests;

/// <summary>
/// Runs <see cref="EdictCommandGenerator"/> over a snippet of consumer source
/// and returns the emitted files as a deterministic name-to-text map, so a
/// Verify snapshot can assert the generated interface/dispatch/surrogate/map.
/// </summary>
internal static class GeneratorTestHarness
{
    public static IReadOnlyDictionary<string, string> Run(string consumerSource)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();

        var compilation = CSharpCompilation.Create(
            assemblyName: "ConsumerUnderTest",
            syntaxTrees: [CSharpSyntaxTree.ParseText(consumerSource)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new EdictCommandGenerator().AsSourceGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();

        return result.GeneratedTrees
            .OrderBy(tree => tree.FilePath, StringComparer.Ordinal)
            .ToDictionary(
                tree => Path.GetFileName(tree.FilePath),
                tree => tree.GetText().ToString());
    }
}
