using System.Linq;

using Edict.Generators.Classification;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Generators.EventStreamAccessors;

internal static class EventStreamAccessorDiscovery
{
    public static EventStreamAccessorModel? MapEventForAccessor(RecordDeclarationSyntax syntax, SemanticModel model)
    {
        if (EdictTypeClassifier.Classify(syntax, model) != EdictTypeKind.Event)
        {
            return null;
        }

        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol eventType)
        {
            return null;
        }

        var streamAttr = eventType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString(FullyQualified)
                == EdictWellKnownNames.EdictStreamAttributeFqn);

        if (streamAttr is null || streamAttr.ConstructorArguments.Length == 0)
        {
            return null;
        }

        var streamName = streamAttr.ConstructorArguments[0].Value as string;
        if (streamName is null)
        {
            return null;
        }

        string? routeKeyProperty = null;
        var routeKeyCount = 0;
        for (INamedTypeSymbol? type = eventType; type is not null; type = type.BaseType)
        {
            if (type.ToDisplayString(FullyQualified) is "global::System.Object")
            {
                break;
            }

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                var attributes = property.GetAttributes();
                if (attributes.Any(a => a.AttributeClass?.ToDisplayString(FullyQualified)
                        == EdictWellKnownNames.EdictRouteKeyAttributeFqn))
                {
                    routeKeyCount++;
                    if (routeKeyProperty is null && property.Type.ToDisplayString(FullyQualified) == "global::System.Guid")
                    {
                        routeKeyProperty = property.Name;
                    }
                }
            }
        }

        if (routeKeyProperty is null || routeKeyCount != 1)
        {
            return null;
        }

        return new EventStreamAccessorModel(
            eventType.ToDisplayString(FullyQualified),
            streamName,
            routeKeyProperty);
    }

    static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat;
}
