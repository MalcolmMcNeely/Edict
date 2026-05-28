using System.Collections.Immutable;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Edict.Analyzers.Interceptors;

/// <summary>
/// EDICT016 — flags <see cref="EdictWellKnownNames.EdictCommandHandlerFqn"/>.Raise
/// call sites whose argument has an abstract static type (e.g. an
/// <c>EdictEvent</c>-typed variable). The Raise interceptor stub (ADR-0034)
/// matches per-event-type; abstract arguments skip the typed call site.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BaseTypedRaiseAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT016",
        title: "EdictCommandHandler.Raise must be called with a concrete-typed event",
        messageFormat: "'Raise' was called with base-typed argument '{0}' — call with a concrete event type so the interceptor fast path (ADR-0034) can intercept the site",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    static void Analyze(OperationAnalysisContext context)
    {
        var op = (IInvocationOperation)context.Operation;
        var method = op.TargetMethod;

        if (method.Name != "Raise" || method.Parameters.Length != 1)
        {
            return;
        }

        var containing = method.ContainingType;
        if (containing is null || !DerivesFromOrIs(containing, EdictWellKnownNames.EdictCommandHandlerFqn))
        {
            return;
        }

        if (op.Arguments.Length != 1)
        {
            return;
        }

        var argType = ResolveSourceType(op.Arguments[0].Value);
        if (argType is null || !DerivesFromOrIs(argType, EdictWellKnownNames.EdictEventFqn))
        {
            return;
        }

        if (!argType.IsAbstract)
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, op.Syntax.GetLocation(), argType.Name));
    }

    static INamedTypeSymbol? ResolveSourceType(IOperation argument)
    {
        var current = argument;
        while (current is IConversionOperation { IsImplicit: true } conv)
        {
            current = conv.Operand;
        }
        return current.Type as INamedTypeSymbol;
    }

    static bool DerivesFromOrIs(INamedTypeSymbol type, string baseFqn)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var fqn = current.IsGenericType
                ? current.OriginalDefinition.ToDisplayString(FullyQualifiedNoGenerics)
                : current.ToDisplayString(FullyQualified);
            if (fqn == baseFqn)
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
