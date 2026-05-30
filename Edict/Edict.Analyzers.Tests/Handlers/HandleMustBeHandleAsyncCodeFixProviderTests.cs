using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Edict.Analyzers.Handlers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace Edict.Analyzers.Tests.Handlers;

public class HandleMustBeHandleAsyncCodeFixProviderTests
{
    [Fact]
    public async Task EDICT018_CodeFix_ShouldRenameHandleToHandleAsync_PreservingParametersAndBody()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Core.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public partial class OrderCommandHandler : EdictCommandHandler
            {
                public Task<EdictCommandResult> Handle(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source);

        Assert.Contains("HandleAsync(PlaceOrder c)", fixedSource);
        Assert.DoesNotContain(" Handle(PlaceOrder c)", fixedSource);
        Assert.Contains("Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted())", fixedSource);
    }

    [Fact]
    public async Task EDICT018_CodeFix_ShouldPreserveAttributesOnMethod()
    {
        const string source = """
            using System;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Core.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public partial class OrderCommandHandler : EdictCommandHandler
            {
                [SuppressMessage("Style", "IDE0001")]
                public Task<EdictCommandResult> Handle(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        var fixedSource = await ApplyCodeFixAsync(source);

        Assert.Contains("[SuppressMessage(\"Style\", \"IDE0001\")]", fixedSource);
        Assert.Contains("HandleAsync(PlaceOrder c)", fixedSource);
    }

    static async Task<string> ApplyCodeFixAsync(string source)
    {
        var references = ((string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();

        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        var workspace = new AdhocWorkspace(host);
        var project = workspace.AddProject("ConsumerUnderTest", LanguageNames.CSharp)
            .WithMetadataReferences(references)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var document = project.AddDocument("Source.cs", SourceText.From(source));

        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new HandleMustBeHandleAsyncAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        var fixer = new HandleMustBeHandleAsyncCodeFixProvider();
        var firstDiagnostic = diagnostics.FirstOrDefault(d => d.Id == HandleMustBeHandleAsyncAnalyzer.DiagnosticId);
        Assert.NotNull(firstDiagnostic);

        CodeAction? registered = null;
        var fixContext = new CodeFixContext(
            document,
            firstDiagnostic!,
            (action, _) => registered ??= action,
            CancellationToken.None);

        await fixer.RegisterCodeFixesAsync(fixContext);

        Assert.NotNull(registered);
        var operations = await registered!.GetOperationsAsync(CancellationToken.None);
        var changedSolution = ((ApplyChangesOperation)operations[0]).ChangedSolution;
        var changedDocument = changedSolution.GetDocument(document.Id)!;
        var changedRoot = await changedDocument.GetSyntaxRootAsync();
        return changedRoot!.ToFullString();
    }
}
