using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Generators.Classification;

/// <summary>
/// Single source of truth for "what kind of Edict type is this declaration?".
/// Replaces the per-generator <c>DerivesFromX</c> helpers that previously lived
/// in each of the seven generators. ADR-0005 still holds — no assembly
/// reference; base types are matched by fully-qualified name.
/// <para>
/// Returns <see cref="EdictTypeKind.None"/> for any node that cannot host
/// generated code (non-partial, missing base list, file-local, non-public /
/// non-internal accessibility chain) or that does not derive from a recognised
/// Edict base.
/// </para>
/// </summary>
public static class EdictTypeClassifier
{
    public static EdictTypeKind Classify(SyntaxNode node, SemanticModel model)
    {
        if (node is not TypeDeclarationSyntax declaration)
        {
            return EdictTypeKind.None;
        }

        if (declaration.BaseList is null)
        {
            return EdictTypeKind.None;
        }

        if (!declaration.Modifiers.Any(static m => m.ValueText == "partial"))
        {
            return EdictTypeKind.None;
        }

        if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
        {
            return EdictTypeKind.None;
        }

        if (symbol.IsFileLocal)
        {
            return EdictTypeKind.None;
        }

        for (INamedTypeSymbol? containing = symbol; containing is not null; containing = containing.ContainingType)
        {
            var accessibility = containing.DeclaredAccessibility;
            if (accessibility != Accessibility.Public && accessibility != Accessibility.Internal)
            {
                return EdictTypeKind.None;
            }
        }

        return ClassifyByBaseChain(symbol);
    }

    static EdictTypeKind ClassifyByBaseChain(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            var fqn = current.ToDisplayString(FullyQualified);
            if (fqn == EdictWellKnownNames.EdictCommandFqn)
            {
                return EdictTypeKind.Command;
            }

            if (fqn == EdictWellKnownNames.EdictEventFqn)
            {
                return EdictTypeKind.Event;
            }

            var genericsStrippedFqn = current.IsGenericType
                ? current.OriginalDefinition.ToDisplayString(FullyQualifiedNoGenerics)
                : fqn;

            if (genericsStrippedFqn == EdictWellKnownNames.EdictCommandHandlerFqn)
            {
                return EdictTypeKind.CommandHandler;
            }

            if (genericsStrippedFqn == EdictWellKnownNames.EdictEventHandlerFqn)
            {
                return EdictTypeKind.EventHandler;
            }

            if (genericsStrippedFqn == EdictWellKnownNames.EdictProjectionBuilderFqn)
            {
                return EdictTypeKind.ProjectionBuilder;
            }

            if (genericsStrippedFqn == EdictWellKnownNames.EdictSagaFqn)
            {
                return EdictTypeKind.Saga;
            }
        }

        return EdictTypeKind.None;
    }

    static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat;

    static readonly SymbolDisplayFormat FullyQualifiedNoGenerics =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);
}
