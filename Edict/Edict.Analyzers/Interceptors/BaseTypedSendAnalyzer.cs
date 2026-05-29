using System.Collections.Immutable;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Edict.Analyzers.Interceptors;

/// <summary>
/// EDICT015 — flags <see cref="EdictWellKnownNames.IEdictSenderFqn"/>.Send
/// call sites whose argument has an abstract static type (e.g. an
/// <c>EdictCommand</c>-typed variable). The interceptor fast path requires
/// concrete-typed call sites; an abstract argument forces the runtime down
/// the registrar dictionary lookup and forfeits the win.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BaseTypedSendAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT015",
        title: "IEdictSender.Send must be called with a concrete-typed command",
        messageFormat: "'IEdictSender.Send' was called with base-typed argument '{0}' — call with a concrete command type so the interceptor fast path can intercept the site",
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

        if (method.Name != "Send" || method.Parameters.Length != 1)
        {
            return;
        }

        var containingFqn = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (containingFqn != EdictWellKnownNames.IEdictSenderFqn)
        {
            return;
        }

        if (op.Arguments.Length != 1)
        {
            return;
        }

        var argType = ResolveSourceType(op.Arguments[0].Value);
        if (argType is null || !DerivesFromOrIs(argType, EdictWellKnownNames.EdictCommandFqn))
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
        // Peel implicit conversions: when the consumer calls Send(concrete),
        // Roslyn wraps the argument in an IConversionOperation up to the
        // declared EdictCommand parameter type. We want the source-side type
        // — the concrete one — not the post-conversion base.
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
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == baseFqn)
            {
                return true;
            }
        }
        return false;
    }
}
