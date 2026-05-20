using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using Edict.Analyzers.Persistence;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace Edict.Analyzers.Tests.Persistence;

public class PersistedStateContractCodeFixProviderTests
{
    [Fact]
    public async Task EDICT011_CodeFix_ShouldInsertEveryMissingAttributeInOneAction_WhenAllArePresent()
    {
        // Composite-case proof: a type missing every consumer-owned attribute
        // resolves in ONE codefix invocation — the batched Quick Action the
        // ADR mandates so the consumer doesn't fix three diagnostics by hand.
        const string source = """
            using Edict.Contracts.Persistence;
            namespace Sample;
            public sealed class OrderState : IEdictPersistedState
            {
                public int Count { get; set; }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source);

        Assert.Contains("[GenerateSerializer]", fixedSource);
        Assert.Contains("[Alias(\"OrderState\")]", fixedSource);
        Assert.Contains("[Id(0)]", fixedSource);
    }

    [Fact]
    public async Task EDICT011_CodeFix_ShouldReplaceNameofWithStringLiteral_WhenAliasUsesNameof()
    {
        const string source = """
            using Edict.Contracts.Persistence;
            using Orleans;
            namespace Sample;
            [GenerateSerializer]
            [Alias(nameof(OrderState))]
            public sealed class OrderState : IEdictPersistedState
            {
                [Id(0)]
                public int Count { get; set; }
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source);

        Assert.Contains("[Alias(\"OrderState\")]", fixedSource);
        Assert.DoesNotContain("nameof(OrderState)", fixedSource);
    }

    static async Task<string> ApplyCodeFixAsync(string source)
    {
        var references = ((string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(System.IO.Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();

        var host = Microsoft.CodeAnalysis.Host.Mef.MefHostServices.Create(
            Microsoft.CodeAnalysis.Host.Mef.MefHostServices.DefaultAssemblies);
        var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace(host);
        var project = workspace.AddProject("ConsumerUnderTest", LanguageNames.CSharp)
            .WithMetadataReferences(references)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var document = project.AddDocument("Source.cs", SourceText.From(source));

        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new PersistedStateContractAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        var fixer = new PersistedStateContractCodeFixProvider();
        var firstDiagnostic = diagnostics.FirstOrDefault(d => d.Id == PersistedStateContractAnalyzer.DiagnosticId);
        if (firstDiagnostic is null)
        {
            return source;
        }

        CodeAction? registered = null;
        var fixContext = new CodeFixContext(
            document,
            firstDiagnostic,
            (action, _) => registered ??= action,
            System.Threading.CancellationToken.None);

        await fixer.RegisterCodeFixesAsync(fixContext);

        Assert.NotNull(registered);
        var operations = await registered!.GetOperationsAsync(System.Threading.CancellationToken.None);
        var changedSolution = ((Microsoft.CodeAnalysis.CodeActions.ApplyChangesOperation)operations[0]).ChangedSolution;
        var changedDocument = changedSolution.GetDocument(document.Id)!;
        var changedRoot = await changedDocument.GetSyntaxRootAsync();
        return changedRoot!.ToFullString();
    }
}
