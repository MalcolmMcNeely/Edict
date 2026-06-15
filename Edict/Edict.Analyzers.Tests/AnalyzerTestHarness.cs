using System.Collections.Immutable;

using Edict.Analyzers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Tests;

internal static class AnalyzerTestHarness
{
    public static ImmutableArray<Diagnostic> Run(string consumerSource, params DiagnosticAnalyzer[] analyzers) =>
        Run(consumerSource, enabledDiagnostics: null, analyzers);

    /// <summary>
    /// Runs the analyzers over the source. An opt-in analyzer
    /// (<c>IsEnabledByDefault: false</c>) is skipped wholesale unless its
    /// diagnostic id is raised to a reporting severity, which is what the
    /// <c>.editorconfig</c> knob does in a real build; pass the id through
    /// <paramref name="enabledDiagnostics"/> to model that opt-in.
    /// </summary>
    public static ImmutableArray<Diagnostic> Run(
        string consumerSource,
        IEnumerable<string>? enabledDiagnostics,
        params DiagnosticAnalyzer[] analyzers)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        if (enabledDiagnostics is not null)
        {
            options = options.WithSpecificDiagnosticOptions(
                enabledDiagnostics.Select(id =>
                    new KeyValuePair<string, ReportDiagnostic>(id, ReportDiagnostic.Error)));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "ConsumerUnderTest",
            syntaxTrees: [CSharpSyntaxTree.ParseText(consumerSource)],
            references: references,
            options: options);

        return compilation
            .WithAnalyzers(ImmutableArray.Create(analyzers))
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }
}
