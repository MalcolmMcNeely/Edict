using System.Collections.Immutable;
using System.Linq;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Sagas;

/// <summary>
/// EDICT022 — overriding <c>OnSagaTimeoutAsync</c> on a saga declared
/// <c>[EdictSagaTimeout(Unbounded = true)]</c> is dead code: an unbounded saga
/// never arms a cap, so the hook can never fire. Only the explicit-unbounded case
/// is detectable — the analyzer cannot see the runtime silo-wide default.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeadSagaTimeoutHookAnalyzer : DiagnosticAnalyzer
{
    const string TimeoutHookName = "OnSagaTimeoutAsync";

    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT022",
        title: "OnSagaTimeoutAsync override is dead code on an unbounded saga",
        messageFormat: "'{0}' is declared [EdictSagaTimeout(Unbounded = true)], so its OnSagaTimeoutAsync override can never fire",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        var attribute = type.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == EdictWellKnownNames.EdictSagaTimeoutAttributeFqn);

        if (attribute is null)
        {
            return;
        }

        var unbounded = attribute.NamedArguments
            .Any(argument => argument.Key == "Unbounded" && argument.Value.Value is true);

        if (!unbounded)
        {
            return;
        }

        var hookOverride = type.GetMembers(TimeoutHookName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.IsOverride);

        if (hookOverride is null)
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, hookOverride.Locations[0], type.Name));
    }
}
