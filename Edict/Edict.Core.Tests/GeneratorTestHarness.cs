using System.Collections.Immutable;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Edict.Core.Tests;

/// <summary>
/// Runs an Edict source generator over a snippet of consumer source and
/// returns the emitted files as a deterministic name-to-text map, so a
/// Verify snapshot can assert the generated output.
/// </summary>
internal static class GeneratorTestHarness
{
    public static IReadOnlyDictionary<string, string> Run(string consumerSource) =>
        Run(consumerSource, new EdictCommandGenerator());

    public static IReadOnlyDictionary<string, string> RunEventGenerator(string consumerSource) =>
        Run(consumerSource, new EdictEventGenerator());

    public static IReadOnlyDictionary<string, string> RunProjectionGenerator(string consumerSource) =>
        Run(consumerSource, new EdictProjectionGenerator());

    private static IReadOnlyDictionary<string, string> Run(
        string consumerSource, IIncrementalGenerator generator)
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
            .Create(generator.AsSourceGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();

        return result.GeneratedTrees
            .OrderBy(tree => tree.FilePath, StringComparer.Ordinal)
            .ToDictionary(
                tree => Path.GetFileName(tree.FilePath),
                tree => tree.GetText().ToString());
    }
}
