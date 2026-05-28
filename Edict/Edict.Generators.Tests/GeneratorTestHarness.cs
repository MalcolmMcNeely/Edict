using System.Collections.Immutable;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Edict.Generators.Tests;

/// <summary>
/// Runs the unified <see cref="EdictGenerator"/> over a snippet of consumer
/// source and returns the emitted files as a deterministic name-to-text map.
/// Each <c>Run*</c> overload filters the unified output to the files the
/// caller's concept-specific Verify snapshot expects, so the existing
/// per-concept snapshots remain byte-identical after the seven-to-one collapse.
/// </summary>
internal static class GeneratorTestHarness
{
    public static IReadOnlyDictionary<string, string> Run(string consumerSource) =>
        RunUnified(consumerSource)
            .Where(kvp => !kvp.Key.EndsWith(".EventHandler.g.cs", StringComparison.Ordinal))
            .Where(kvp => !kvp.Key.EndsWith(".Saga.g.cs", StringComparison.Ordinal))
            .Where(kvp => !kvp.Key.EndsWith("EdictEventStreamRegistrar.g.cs", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    public static IReadOnlyDictionary<string, string> RunEventGenerator(string consumerSource) =>
        RunUnified(consumerSource)
            .Where(kvp => kvp.Key.EndsWith(".Alias.g.cs", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    public static IReadOnlyDictionary<string, string> RunProjectionGenerator(string consumerSource) =>
        RunUnified(consumerSource)
            .Where(kvp => !kvp.Key.EndsWith(".Alias.g.cs", StringComparison.Ordinal))
            .Where(kvp => !kvp.Key.EndsWith("EdictEventStreamRegistrar.g.cs", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    public static IReadOnlyDictionary<string, string> RunEventHandlerGenerator(string consumerSource) =>
        RunUnified(consumerSource)
            .Where(kvp => kvp.Key.EndsWith(".EventHandler.g.cs", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    public static IReadOnlyDictionary<string, string> RunEdictEventStreamAccessorsGenerator(string consumerSource) =>
        RunUnified(consumerSource)
            .Where(kvp => kvp.Key.EndsWith("EdictEventStreamRegistrar.g.cs", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    private static IReadOnlyDictionary<string, string> RunUnified(string consumerSource)
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
            .Create(new EdictGenerator().AsSourceGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();

        return result.GeneratedTrees
            .OrderBy(tree => tree.FilePath, StringComparer.Ordinal)
            .ToDictionary(
                tree => Path.GetFileName(tree.FilePath),
                tree => tree.GetText().ToString());
    }
}
