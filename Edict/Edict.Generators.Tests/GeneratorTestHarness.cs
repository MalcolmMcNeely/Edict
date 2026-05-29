using System.Collections.Immutable;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

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
            .Where(kvp => !kvp.Key.EndsWith(".SendInterceptor.g.cs", StringComparison.Ordinal))
            .Where(kvp => !kvp.Key.EndsWith(".RaiseInterceptor.g.cs", StringComparison.Ordinal))
            .Where(kvp => !kvp.Key.EndsWith(".DispatchInterceptor.g.cs", StringComparison.Ordinal))
            .Where(kvp => kvp.Key != "Edict.Generated.InterceptsLocationAttribute.g.cs")
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

    public static IReadOnlyDictionary<string, string> RunEventTagWritersGenerator(string consumerSource) =>
        RunUnified(consumerSource)
            .Where(kvp => kvp.Key.EndsWith("EdictEventTagWritersRegistrar.g.cs", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    public static IReadOnlyDictionary<string, string> RunSendInterceptorGenerator(
        string consumerSource, bool interceptorsEnabled = true) =>
        RunUnified(consumerSource, interceptorsEnabled)
            .Where(kvp => kvp.Key.EndsWith(".SendInterceptor.g.cs", StringComparison.Ordinal)
                       || kvp.Key == "Edict.Generated.InterceptsLocationAttribute.g.cs")
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    public static IReadOnlyDictionary<string, string> RunRaiseInterceptorGenerator(
        string consumerSource, bool interceptorsEnabled = true) =>
        RunUnified(consumerSource, interceptorsEnabled)
            .Where(kvp => kvp.Key.EndsWith(".RaiseInterceptor.g.cs", StringComparison.Ordinal)
                       || kvp.Key == "Edict.Generated.InterceptsLocationAttribute.g.cs")
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    public static IReadOnlyDictionary<string, string> RunDispatchInterceptorGenerator(
        string consumerSource, bool interceptorsEnabled = true) =>
        RunUnified(consumerSource, interceptorsEnabled)
            .Where(kvp => kvp.Key.EndsWith(".DispatchInterceptor.g.cs", StringComparison.Ordinal)
                       || kvp.Key == "Edict.Generated.InterceptsLocationAttribute.g.cs")
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    static IReadOnlyDictionary<string, string> RunUnified(
        string consumerSource, bool interceptorsEnabled = true)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();

        // Stable, non-empty file path so the InterceptableLocation base64 data
        // does not encode build-host noise — the Verify snapshot must remain
        // byte-identical across machines. Also normalise CRLF → LF so the
        // SHA-256 over the syntax-tree source matches between a Windows
        // checkout (raw-string-literal source has CRLF) and a Linux CI
        // checkout (LF) — without it, the InterceptableLocation hash drifts.
        var normalisedSource = consumerSource.Replace("\r\n", "\n");

        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithFeatures(new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", "Edict.Generated") });

        var compilation = CSharpCompilation.Create(
            assemblyName: "ConsumerUnderTest",
            syntaxTrees: [CSharpSyntaxTree.ParseText(normalisedSource, parseOptions, path: "Consumer.cs")],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var optionsProvider = new InterceptorsToggleOptionsProvider(interceptorsEnabled);

        var driver = CSharpGeneratorDriver.Create(
            generators: [new EdictGenerator().AsSourceGenerator()],
            additionalTexts: ImmutableArray<AdditionalText>.Empty,
            parseOptions: parseOptions,
            optionsProvider: optionsProvider);

        var ranDriver = driver.RunGenerators(compilation);

        var result = ranDriver.GetRunResult();

        return result.GeneratedTrees
            .OrderBy(tree => tree.FilePath, StringComparer.Ordinal)
            .ToDictionary(
                tree => Path.GetFileName(tree.FilePath),
                tree => tree.GetText().ToString());
    }

    sealed class InterceptorsToggleOptionsProvider(bool interceptorsEnabled) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } =
            new ToggleOptions(interceptorsEnabled);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        sealed class ToggleOptions(bool interceptorsEnabled) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.EdictInterceptorsEnabled")
                {
                    value = interceptorsEnabled ? "true" : "false";
                    return true;
                }
                value = null!;
                return false;
            }
        }
    }
}
