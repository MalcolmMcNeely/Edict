using Edict.Generators.Classification;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Generators.Events;

internal static class EventDiscovery
{
    public static EventModel? MapEventRecord(RecordDeclarationSyntax syntax, SemanticModel model)
    {
        if (EdictTypeClassifier.Classify(syntax, model) != EdictTypeKind.Event)
        {
            return null;
        }

        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol eventType)
        {
            return null;
        }

        var ns = eventType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : eventType.ContainingNamespace.ToDisplayString();

        return new EventModel(eventType.Name, ns);
    }
}
