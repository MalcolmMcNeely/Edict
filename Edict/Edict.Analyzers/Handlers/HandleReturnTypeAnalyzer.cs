using System.Collections.Immutable;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Handlers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandleReturnTypeAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT002",
        title: "Handle method must return Task<EdictCommandResult>",
        messageFormat: "Handle method for '{0}' in '{1}' must return Task<EdictCommandResult>",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.Method);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        if (method.Name != "Handle" || method.Parameters.Length != 1)
        {
            return;
        }

        if (method.ContainingType.BaseType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                != EdictWellKnownNames.EdictCommandHandlerFqn)
        {
            return;
        }

        var paramType = method.Parameters[0].Type as INamedTypeSymbol;
        if (paramType is null || !DerivesFromCommand(paramType))
        {
            return;
        }

        if (method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                != EdictWellKnownNames.TaskOfEdictCommandResultFqn)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, method.Locations[0],
                    paramType.Name, method.ContainingType.Name));
        }
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
}
