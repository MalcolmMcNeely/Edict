using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RouteKeyAnalyzer : DiagnosticAnalyzer
{
    private const string GuidFqn = "global::System.Guid";

    internal static readonly DiagnosticDescriptor MissingRouteKey = new DiagnosticDescriptor(
        id: "EDICT003",
        title: "Command must have exactly one [EdictRouteKey] property",
        messageFormat: "'{0}' has no [EdictRouteKey] property; exactly one Guid property must carry [EdictRouteKey]",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MultipleRouteKeys = new DiagnosticDescriptor(
        id: "EDICT003",
        title: "Command must have exactly one [EdictRouteKey] property",
        messageFormat: "'{0}' has multiple [EdictRouteKey] properties; exactly one is allowed",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor RouteKeyMustBeGuid = new DiagnosticDescriptor(
        id: "EDICT003",
        title: "[EdictRouteKey] property must be of type Guid",
        messageFormat: "[EdictRouteKey] property '{0}' on '{1}' must be of type Guid",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MissingRouteKey, MultipleRouteKeys, RouteKeyMustBeGuid);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.IsAbstract || (!DerivesFromCommand(type) && !DerivesFromEvent(type)))
        {
            return;
        }

        var routeKeyProperties = CollectRouteKeyProperties(type);

        if (routeKeyProperties.Count == 0)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(MissingRouteKey, type.Locations[0], type.Name));
            return;
        }

        if (routeKeyProperties.Count > 1)
        {
            foreach (var prop in routeKeyProperties)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(MultipleRouteKeys, prop.Locations[0], type.Name));
            }
            return;
        }

        var single = routeKeyProperties[0];
        if (single.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != GuidFqn)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(RouteKeyMustBeGuid, single.Locations[0],
                    single.Name, type.Name));
        }
    }

    private static List<IPropertySymbol> CollectRouteKeyProperties(INamedTypeSymbol type)
    {
        var result = new List<IPropertySymbol>();

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is "global::System.Object")
            {
                break;
            }

            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.GetAttributes().Any(a =>
                        a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        == EdictWellKnownNames.EdictRouteKeyAttributeFqn))
                {
                    result.Add(member);
                }
            }
        }

        return result;
    }

    private static bool DerivesFromCommand(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == EdictWellKnownNames.EdictCommandFqn)
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFromEvent(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == EdictWellKnownNames.EdictEventFqn)
            {
                return true;
            }
        }

        return false;
    }
}
