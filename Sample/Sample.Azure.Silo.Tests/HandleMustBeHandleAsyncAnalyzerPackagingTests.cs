using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Edict.Analyzers.Handlers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using Xunit;

namespace Sample.Azure.Silo.Tests;

/// <summary>
/// Asserts that the EDICT018 analyzer is reachable from a consumer-shaped test
/// project — i.e. that <c>Edict.Analyzers</c> is actually packaged and loadable
/// the same way a real consumer build would see it. The test compiles a
/// handler whose method is intentionally named <c>Handle</c> (the post-rename
/// wrong name) and asserts the analyzer fires the build-breaking diagnostic.
/// Without this test the analyzer could silently regress to "compiles but
/// absent" while every other test remained green.
/// </summary>
public class HandleMustBeHandleAsyncAnalyzerPackagingTests
{
    [Fact]
    public async Task EDICT018_ShouldFireOnConsumerShapedHandlerNamedHandle()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Core.Commands;
            namespace Sample.Negative;
            public sealed record DemoCommand(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public partial class DemoCommandHandler : EdictCommandHandler
            {
                public Task<EdictCommandResult> Handle(DemoCommand c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();

        var compilation = CSharpCompilation.Create(
            assemblyName: "ConsumerShapedNegative",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new HandleMustBeHandleAsyncAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT018", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }
}
