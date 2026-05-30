using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Routing;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateCommandRouteAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT004",
        title: "Command handled by multiple grains",
        messageFormat: "'{0}' is already handled by '{1}'; each command must route to exactly one grain",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(Analyze);
    }

    static void Analyze(CompilationAnalysisContext context)
    {
        var firstHandler = new Dictionary<string, string>();

        foreach (var type in GetAllTypes(context.Compilation.GlobalNamespace))
        {
            if (type.BaseType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    != EdictWellKnownNames.EdictCommandHandlerFqn)
            {
                continue;
            }

            foreach (var member in type.GetMembers(EdictWellKnownNames.HandleMethodName).OfType<IMethodSymbol>())
            {
                if (member.Parameters.Length != 1)
                {
                    continue;
                }

                var paramType = member.Parameters[0].Type as INamedTypeSymbol;
                if (paramType is null || !DerivesFromCommand(paramType))
                {
                    continue;
                }

                var commandFqn = paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (firstHandler.TryGetValue(commandFqn, out var existingGrain))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(Rule, member.Locations[0],
                            paramType.Name, existingGrain));
                }
                else
                {
                    firstHandler[commandFqn] = type.Name;
                }
            }
        }
    }

    static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in GetAllTypes(child))
            {
                yield return type;
            }
        }
    }

    static bool DerivesFromCommand(INamedTypeSymbol type)
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
}
