using System.Linq;

using Microsoft.CodeAnalysis;

namespace Edict.Generators;

// A route key reaches its grain or stream key as the "N"-format string of a Guid:
// a bare Guid directly, or the single Guid a typed route-key struct wraps. Suffix
// returns the member-access tail the emitters append after the cast-and-property
// access, so each emit site stays one concatenation that ends inside Compose.
internal static class RouteKeyStringification
{
    const string GuidFqn = "global::System.Guid";

    public static string Suffix(ITypeSymbol routeKeyType)
    {
        if (IsGuid(routeKeyType))
        {
            return ".ToString(\"N\")";
        }

        if (SingleWrappedGuidProperty(routeKeyType) is { } inner)
        {
            return $".{inner.Name}.ToString(\"N\")";
        }

        return ".ToString(\"N\")";
    }

    // A typed route key is a struct exposing exactly one Guid; Guid itself is the
    // un-wrapped case. Anything else is rejected by EDICT003 before it reaches here.
    public static bool IsRouteKeyType(ITypeSymbol type) =>
        IsGuid(type) || SingleWrappedGuidProperty(type) is not null;

    static bool IsGuid(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == GuidFqn;

    static IPropertySymbol? SingleWrappedGuidProperty(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } named)
        {
            return null;
        }

        var guidProperties = named.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property =>
                !property.IsStatic
                && property.DeclaredAccessibility == Accessibility.Public
                && IsGuid(property.Type))
            .ToList();

        return guidProperties.Count == 1 ? guidProperties[0] : null;
    }
}
