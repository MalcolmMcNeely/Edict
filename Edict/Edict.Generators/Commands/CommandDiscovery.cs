using System.Collections.Generic;
using System.Linq;

using Edict.Generators.Classification;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Generators.Commands;

internal static class CommandDiscovery
{
    public static CommandModel? MapCommandRecord(RecordDeclarationSyntax syntax, SemanticModel model)
    {
        if (EdictTypeClassifier.Classify(syntax, model) != EdictTypeKind.Command)
        {
            return null;
        }

        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol command)
        {
            return null;
        }

        return MapCommand(command);
    }

    public static CommandHandlerGrainModel? MapCommandHandler(ClassDeclarationSyntax syntax, SemanticModel model)
    {
        if (EdictTypeClassifier.Classify(syntax, model) != EdictTypeKind.CommandHandler)
        {
            return null;
        }

        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol grain)
        {
            return null;
        }

        var commands = new List<CommandModel>();

        foreach (var method in grain.GetMembers("Handle").OfType<IMethodSymbol>())
        {
            if (method.Parameters.Length != 1)
            {
                continue;
            }

            if (method.ReturnType.ToDisplayString(FullyQualified) != EdictWellKnownNames.TaskOfEdictCommandResultFqn)
            {
                continue;
            }

            var commandType = method.Parameters[0].Type as INamedTypeSymbol;
            if (commandType is null || !DerivesFromCommand(commandType))
            {
                continue;
            }

            var command = MapCommand(commandType);
            if (command is not null && commands.All(c => c.Fqn != command.Fqn))
            {
                commands.Add(command);
            }
        }

        if (commands.Count == 0)
        {
            return null;
        }

        commands.Sort(static (a, b) => string.CompareOrdinal(a.SimpleName, b.SimpleName));

        var grainNamespace = grain.ContainingNamespace.IsGlobalNamespace
            ? "Edict.Generated"
            : grain.ContainingNamespace.ToDisplayString();

        var grainFqn = grain.ToDisplayString(FullyQualified);

        return new CommandHandlerGrainModel(
            grainNamespace,
            grain.Name,
            // Orleans matches GetGrain's class-name prefix against the grain's
            // full type name. We can't emit a [GrainType] (Orleans' own codegen
            // is a sibling generator and never sees our generated partial), so
            // the route carries the full type name and the sender uses it to
            // disambiguate the many grain classes sharing IEdictCommandHandler.
            grainFqn.StartsWith("global::", System.StringComparison.Ordinal)
                ? grainFqn.Substring("global::".Length)
                : grainFqn,
            grainFqn,
            new EquatableArray<CommandModel>(commands));
    }

    static CommandModel? MapCommand(INamedTypeSymbol command)
    {
        string? routeKeyProperty = null;
        var telemeterizedProperties = new List<TelemeterizedProperty>();

        for (INamedTypeSymbol? type = command; type is not null; type = type.BaseType)
        {
            if (type.ToDisplayString(FullyQualified) is "global::System.Object")
            {
                break;
            }

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                var attributes = property.GetAttributes();

                if (routeKeyProperty is null &&
                    attributes.Any(a => a.AttributeClass?.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictRouteKeyAttributeFqn))
                {
                    routeKeyProperty = property.Name;
                }

                if (IsPrimitiveType(property.Type) &&
                    attributes.Any(a => a.AttributeClass?.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictTelemeterizedAttributeFqn))
                {
                    telemeterizedProperties.Add(new TelemeterizedProperty(property.Name));
                }
            }
        }

        if (routeKeyProperty is null)
        {
            return null;
        }

        var commandNamespace = command.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : command.ContainingNamespace.ToDisplayString();

        return new CommandModel(
            command.ToDisplayString(FullyQualified),
            command.Name,
            commandNamespace,
            routeKeyProperty,
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

    static bool DerivesFromCommand(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictCommandFqn)
            {
                return true;
            }
        }

        return false;
    }

    static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat;
}
