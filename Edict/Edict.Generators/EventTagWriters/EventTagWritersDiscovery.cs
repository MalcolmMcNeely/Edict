using System.Collections.Generic;
using System.Linq;

using Edict.Generators.Classification;
using Edict.Generators.Commands;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Generators.EventTagWriters;

internal static class EventTagWritersDiscovery
{
    public static EventTagWritersModel? MapEvent(RecordDeclarationSyntax syntax, SemanticModel model)
    {
        if (EdictTypeClassifier.Classify(syntax, model) != EdictTypeKind.Event)
        {
            return null;
        }

        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol eventType)
        {
            return null;
        }

        var telemeterizedProperties = new List<TelemeterizedProperty>();
        var seenNames = new HashSet<string>(System.StringComparer.Ordinal);
        for (INamedTypeSymbol? type = eventType; type is not null; type = type.BaseType)
        {
            if (type.ToDisplayString(FullyQualified) is "global::System.Object")
            {
                break;
            }

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (!seenNames.Add(property.Name))
                {
                    continue;
                }

                if (!IsPrimitiveType(property.Type))
                {
                    continue;
                }

                var attributes = property.GetAttributes();
                if (attributes.Any(a => a.AttributeClass?.ToDisplayString(FullyQualified)
                        == EdictWellKnownNames.EdictTelemeterizedAttributeFqn))
                {
                    telemeterizedProperties.Add(new TelemeterizedProperty(property.Name));
                }
            }
        }

        if (telemeterizedProperties.Count == 0)
        {
            return null;
        }

        return new EventTagWritersModel(
            eventType.ToDisplayString(FullyQualified),
            new EquatableArray<TelemeterizedProperty>(telemeterizedProperties));
    }

    static bool IsPrimitiveType(ITypeSymbol type) =>
        type.SpecialType is
            SpecialType.System_String or
            SpecialType.System_Boolean or
            SpecialType.System_Byte or
            SpecialType.System_SByte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal or
            SpecialType.System_Char
        || type.ToDisplayString(FullyQualified) == "global::System.Guid";

    static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat;
}
