using System.Collections.Generic;
using System.Linq;

using Edict.Generators.Classification;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Generators.Projections;

internal static class ProjectionDiscovery
{
    public static ProjectionGrainModel? MapProjection(ClassDeclarationSyntax syntax, SemanticModel model)
    {
        if (EdictTypeClassifier.Classify(syntax, model) != EdictTypeKind.ProjectionBuilder)
        {
            return null;
        }

        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol grain)
        {
            return null;
        }

        var handlers = new List<ProjectionHandlerModel>();

        foreach (var method in grain.GetMembers(EdictWellKnownNames.HandleMethodName).OfType<IMethodSymbol>())
        {
            if (method.Parameters.Length != 1)
            {
                continue;
            }

            if (method.ReturnType.ToDisplayString(FullyQualified) != EdictWellKnownNames.TaskFqn)
            {
                continue;
            }

            if (method.Parameters[0].Type is not INamedTypeSymbol eventType)
            {
                continue;
            }

            if (!DerivesFromEvent(eventType))
            {
                continue;
            }

            var streamName = GetStreamName(eventType);
            if (streamName is null)
            {
                continue;
            }

            var handler = new ProjectionHandlerModel(
                eventType.ToDisplayString(FullyQualified),
                eventType.Name,
                streamName);

            if (handlers.All(h => h.EventFqn != handler.EventFqn))
            {
                handlers.Add(handler);
            }
        }

        if (handlers.Count == 0)
        {
            return null;
        }

        handlers.Sort(static (a, b) => System.StringComparer.Ordinal.Compare(a.EventSimpleName, b.EventSimpleName));

        var grainNamespace = grain.ContainingNamespace.IsGlobalNamespace
            ? "Edict.Generated"
            : grain.ContainingNamespace.ToDisplayString();

        var grainFqn = grain.ToDisplayString(FullyQualified);
        var grainTypeName = grainFqn.StartsWith("global::", System.StringComparison.Ordinal)
            ? grainFqn.Substring("global::".Length)
            : grainFqn;

        return new ProjectionGrainModel(
            grainNamespace,
            grain.Name,
            grainTypeName,
            FindReadModelFqn(grain),
            new EquatableArray<ProjectionHandlerModel>(handlers));
    }

    // Both species carry a read-route keyed by their read-model type, taken from
    // the sole type argument of the species base: the row type for
    // EdictListProjectionBuilder<TListProjection>, the projection type for the
    // in-grain EdictProjectionBuilder<TProjection>. A projection on the abstract
    // root directly (a bespoke grain-interface projection) matches neither and so
    // carries no read-route.
    static string? FindReadModelFqn(INamedTypeSymbol grain)
    {
        for (var current = grain.BaseType; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType)
            {
                continue;
            }

            var openFqn = current.OriginalDefinition.ToDisplayString(FullyQualifiedNoGenerics);
            if (openFqn == EdictWellKnownNames.EdictListProjectionBuilderFqn
                || openFqn == EdictWellKnownNames.EdictStateProjectionBuilderFqn)
            {
                return current.TypeArguments[0].ToDisplayString(FullyQualified);
            }
        }

        return null;
    }

    static string? GetStreamName(INamedTypeSymbol eventType)
    {
        var attr = eventType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictStreamAttributeFqn);

        return attr?.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value as string
            : null;
    }

    static bool DerivesFromEvent(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictEventFqn)
            {
                return true;
            }
        }

        return false;
    }

    static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat;

    static readonly SymbolDisplayFormat FullyQualifiedNoGenerics =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);
}
