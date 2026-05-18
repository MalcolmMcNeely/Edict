using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProjectionHandleSignatureAnalyzer : DiagnosticAnalyzer
{
    private const string ProjectionBuilderGrainFqn = "global::Edict.Core.Grains.ProjectionBuilderGrain";
    private const string EventFqn = "global::Edict.Contracts.Events.Event";
    private const string TaskFqn = "global::System.Threading.Tasks.Task";

    internal static readonly DiagnosticDescriptor WrongReturnType = new DiagnosticDescriptor(
        id: "EDICT009",
        title: "Projection Builder Handle must return Task",
        messageFormat: "Handle method for '{0}' in '{1}' must return Task, not Task<T>",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NonEventParameter = new DiagnosticDescriptor(
        id: "EDICT009",
        title: "Projection Builder Handle parameter must derive from Event",
        messageFormat: "Handle method for '{0}' in '{1}' must take an Event-derived parameter",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(WrongReturnType, NonEventParameter);

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

        if (!DerivesFrom(method.ContainingType, ProjectionBuilderGrainFqn))
        {
            return;
        }

        var paramType = method.Parameters[0].Type as INamedTypeSymbol;
        var returnFqn = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (returnFqn != TaskFqn)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(WrongReturnType, method.Locations[0],
                    paramType?.Name ?? method.Parameters[0].Type.Name,
                    method.ContainingType.Name));
            return;
        }

        if (paramType is null || !DerivesFromEvent(paramType))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(NonEventParameter, method.Locations[0],
                    method.Parameters[0].Type.Name,
                    method.ContainingType.Name));
        }
    }

    private static bool DerivesFrom(INamedTypeSymbol type, string fqn)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fqn
                || current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fqn)
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
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == EventFqn)
            {
                return true;
            }
        }

        return false;
    }
}
