using System.Collections.Immutable;

using Edict.Analyzers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Tests;

internal static class AnalyzerTestHarness
{
    public static ImmutableArray<Diagnostic> Run(string consumerSource, params DiagnosticAnalyzer[] analyzers)
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

        return compilation
            .WithAnalyzers(ImmutableArray.Create(analyzers))
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }
}
