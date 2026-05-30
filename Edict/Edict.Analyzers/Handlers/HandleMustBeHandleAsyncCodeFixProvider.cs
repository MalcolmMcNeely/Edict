using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Analyzers.Handlers;

/// <summary>
/// Code-fix for EDICT018: renames the offending <c>Handle</c> method to
/// <c>HandleAsync</c> in place, preserving parameters, body, attributes and
/// trivia. The rename is a single-identifier edit so the consumer's first
/// reaction to the diagnostic is one Quick Action.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HandleMustBeHandleAsyncCodeFixProvider))]
[Shared]
public sealed class HandleMustBeHandleAsyncCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(HandleMustBeHandleAsyncAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var diagnosticSpan = context.Diagnostics[0].Location.SourceSpan;
        var node = root.FindNode(diagnosticSpan);
        var methodDeclaration = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDeclaration is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Rename 'Handle' to 'HandleAsync'",
                createChangedDocument: cancellationToken => RenameAsync(context.Document, methodDeclaration, cancellationToken),
                equivalenceKey: "Edict.EDICT018.Rename"),
            context.Diagnostics);
    }

    static async Task<Document> RenameAsync(Document document, MethodDeclarationSyntax methodDeclaration, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var newIdentifier = SyntaxFactory.Identifier("HandleAsync")
            .WithLeadingTrivia(methodDeclaration.Identifier.LeadingTrivia)
            .WithTrailingTrivia(methodDeclaration.Identifier.TrailingTrivia);

        var renamed = methodDeclaration.WithIdentifier(newIdentifier);
        var newRoot = root.ReplaceNode(methodDeclaration, renamed);

        return document.WithSyntaxRoot(newRoot);
    }
}
