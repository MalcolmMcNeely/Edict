using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Edict.Generators.Tests;

/// <summary>
/// Runs an Edict source generator over a canonical consumer snippet, applies
/// an unrelated edit (a comment-edit in a sibling syntax tree), runs the
/// driver a second time, and asserts that every tracked output step reports
/// <see cref="IncrementalStepRunReason.Cached"/> or
/// <see cref="IncrementalStepRunReason.Unchanged"/>.
/// <para>
/// A <see cref="IncrementalStepRunReason.New"/>,
/// <see cref="IncrementalStepRunReason.Modified"/> or
/// <see cref="IncrementalStepRunReason.Removed"/> reason on the second run
/// means the generator did real work for inputs it had already seen — i.e.
/// it closes over the <see cref="Compilation"/> or an
/// <see cref="ISymbol"/> in a transform, or subscribes directly to
/// <see cref="IncrementalGeneratorInitializationContext.CompilationProvider"/>,
/// any of which silently breaks incremental caching.
/// </para>
/// </summary>
internal static class GeneratorIncrementalityHarness
{
    public static void AssertCachedOnUnrelatedEdit<TGenerator>(string canonicalSource)
        where TGenerator : IIncrementalGenerator, new()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

        var canonicalTree = CSharpSyntaxTree.ParseText(canonicalSource, parseOptions);
        var unrelatedV1 = CSharpSyntaxTree.ParseText(UnrelatedV1, parseOptions);

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorIncrementalityHarnessAssembly",
            syntaxTrees: [canonicalTree, unrelatedV1],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            generators: [new TGenerator().AsSourceGenerator()],
            additionalTexts: ImmutableArray<AdditionalText>.Empty,
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        // Apply an unrelated edit: replace the sibling tree with a version
        // that only adds a comment. The canonical tree is untouched, so a
        // correctly-incremental generator must surface every tracked output
        // as Cached or Unchanged on the second run.
        var unrelatedV2 = CSharpSyntaxTree.ParseText(UnrelatedV2, parseOptions);
        var editedCompilation = compilation.ReplaceSyntaxTree(unrelatedV1, unrelatedV2);

        driver = (CSharpGeneratorDriver)driver.RunGenerators(editedCompilation);

        var runResult = driver.GetRunResult().Results.Single();

        var violations = new List<string>();

        foreach (var (outputName, outputSteps) in runResult.TrackedOutputSteps)
        {
            for (var stepIndex = 0; stepIndex < outputSteps.Length; stepIndex++)
            {
                var step = outputSteps[stepIndex];
                for (var outputIndex = 0; outputIndex < step.Outputs.Length; outputIndex++)
                {
                    var reason = step.Outputs[outputIndex].Reason;
                    if (reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)
                    {
                        continue;
                    }

                    violations.Add(
                        $"{outputName}[{stepIndex}].Outputs[{outputIndex}] reason = {reason}");
                }
            }
        }

        if (violations.Count > 0)
        {
            throw new GeneratorIncrementalityException(
                $"{typeof(TGenerator).Name} broke incrementality on an unrelated edit:\n  - "
                + string.Join("\n  - ", violations));
        }
    }

    private const string UnrelatedV1 =
        "namespace Edict.Generators.Tests.HarnessUnrelated { internal sealed class Sentinel { } }";

    private const string UnrelatedV2 =
        "namespace Edict.Generators.Tests.HarnessUnrelated { internal sealed class Sentinel { /* edit */ } }";
}
