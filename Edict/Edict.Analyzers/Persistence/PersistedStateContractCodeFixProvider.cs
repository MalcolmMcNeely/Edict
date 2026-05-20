using System.Collections.Generic;
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
using Microsoft.CodeAnalysis.Formatting;

namespace Edict.Analyzers.Persistence;

/// <summary>
/// One-shot codefix for the EDICT011 family: looks at the persisted-state type
/// at the diagnostic, computes whichever of <c>[GenerateSerializer]</c>,
/// <c>[Alias("...")]</c>, and per-property <c>[Id(n)]</c> are missing or wrong,
/// and inserts them in a single Quick Action. The alias is defaulted to the
/// simple class name as a string literal so the consumer's first rename is a
/// deliberate edit, not a silent break; <c>[Id(n)]</c> assigns the next free
/// integer in declaration order (subsequent properties are the consumer's
/// responsibility — the codefix does not renumber).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PersistedStateContractCodeFixProvider))]
[Shared]
public sealed class PersistedStateContractCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(PersistedStateContractAnalyzer.DiagnosticId);

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
        var typeDecl = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Apply persisted-state attributes (EDICT011)",
                createChangedDocument: ct => ApplyAsync(context.Document, typeDecl, ct),
                equivalenceKey: "Edict.EDICT011.Apply"),
            context.Diagnostics);
    }

    static async Task<Document> ApplyAsync(Document document, TypeDeclarationSyntax originalType, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return document;
        }

        var typeDecl = originalType;
        var symbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
        if (symbol is null)
        {
            return document;
        }

        var attributes = symbol.GetAttributes();
        var hasGenerateSerializer = attributes.Any(a => MatchesAttribute(a, "global::Orleans.GenerateSerializerAttribute"));
        var aliasAttribute = attributes.FirstOrDefault(a => MatchesAttribute(a, "global::Orleans.AliasAttribute"));

        var attributesToPrepend = new List<AttributeListSyntax>();

        if (!hasGenerateSerializer)
        {
            attributesToPrepend.Add(SimpleAttribute("GenerateSerializer"));
        }

        if (aliasAttribute is null)
        {
            attributesToPrepend.Add(AliasAttribute(symbol.Name));
        }
        else if (!IsStringLiteralArgument(aliasAttribute))
        {
            // The nameof(...) defeat: replace the argument with a frozen
            // literal defaulted to the simple class name. The consumer edits
            // this once if they want a fully-qualified flavour, but it now
            // survives a class rename — the whole point of ADR 0027.
            var oldSyntax = aliasAttribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
            if (oldSyntax is not null)
            {
                var newAttribute = oldSyntax.WithArgumentList(
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.StringLiteralExpression,
                                    SyntaxFactory.Literal(symbol.Name))))));
                root = root.ReplaceNode(oldSyntax, newAttribute);
                // Recompute the new type declaration in the rewritten tree.
                typeDecl = (TypeDeclarationSyntax)root.FindNode(originalType.Identifier.Span)
                    .AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();
            }
        }

        // Existing [Id(n)] integers across declared properties — used to pick
        // the next free integer for any newly tagged property.
        var declaredIds = new HashSet<int>();
        foreach (var prop in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (!SymbolEqualityComparer.Default.Equals(prop.ContainingType, symbol))
            {
                continue;
            }
            foreach (var attr in prop.GetAttributes())
            {
                if (MatchesAttribute(attr, "global::Orleans.IdAttribute")
                    && attr.ConstructorArguments.Length > 0
                    && attr.ConstructorArguments[0].Value is int existing)
                {
                    declaredIds.Add(existing);
                }
            }
        }

        var nextId = 0;
        var rewrittenProperties = new Dictionary<SyntaxNode, SyntaxNode>();

        foreach (var property in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (property.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
            {
                continue;
            }

            if (!property.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
            {
                continue;
            }

            var alreadyTagged = property.AttributeLists
                .SelectMany(a => a.Attributes)
                .Any(a => a.Name.ToString().EndsWith("Id"));

            if (alreadyTagged)
            {
                continue;
            }

            while (declaredIds.Contains(nextId))
            {
                nextId++;
            }

            var idList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Id"))
                            .WithArgumentList(
                                SyntaxFactory.AttributeArgumentList(
                                    SyntaxFactory.SingletonSeparatedList(
                                        SyntaxFactory.AttributeArgument(
                                            SyntaxFactory.LiteralExpression(
                                                SyntaxKind.NumericLiteralExpression,
                                                SyntaxFactory.Literal(nextId))))))))
                .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            declaredIds.Add(nextId);
            nextId++;

            var updated = property.WithAttributeLists(property.AttributeLists.Insert(0, idList));
            rewrittenProperties[property] = updated;
        }

        if (rewrittenProperties.Count > 0)
        {
            typeDecl = (TypeDeclarationSyntax)root.FindNode(originalType.Identifier.Span)
                .AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();
            typeDecl = typeDecl.ReplaceNodes(rewrittenProperties.Keys, (orig, _) => rewrittenProperties[orig]);
        }

        if (attributesToPrepend.Count > 0)
        {
            var newAttributeLists = typeDecl.AttributeLists;
            foreach (var attr in attributesToPrepend)
            {
                newAttributeLists = newAttributeLists.Insert(0, attr);
            }
            typeDecl = typeDecl.WithAttributeLists(newAttributeLists);
        }

        var originalInRoot = root.FindNode(originalType.Identifier.Span)
            .AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First();
        root = root.ReplaceNode(originalInRoot, typeDecl.WithAdditionalAnnotations(Formatter.Annotation));

        return document.WithSyntaxRoot(root);
    }

    static AttributeListSyntax SimpleAttribute(string name) =>
        SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(name))))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

    static AttributeListSyntax AliasAttribute(string literal) =>
        SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Alias"))
                        .WithArgumentList(
                            SyntaxFactory.AttributeArgumentList(
                                SyntaxFactory.SingletonSeparatedList(
                                    SyntaxFactory.AttributeArgument(
                                        SyntaxFactory.LiteralExpression(
                                            SyntaxKind.StringLiteralExpression,
                                            SyntaxFactory.Literal(literal))))))))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

    static bool MatchesAttribute(AttributeData attribute, string fqn) =>
        attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fqn;

    static bool IsStringLiteralArgument(AttributeData aliasAttribute)
    {
        if (aliasAttribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax)
        {
            return false;
        }

        var arg = syntax.ArgumentList?.Arguments.FirstOrDefault();
        return arg is not null && arg.Expression.IsKind(SyntaxKind.StringLiteralExpression);
    }
}
