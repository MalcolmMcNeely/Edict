using System.Collections.Immutable;
using System.Linq;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Sagas;

/// <summary>
/// EDICT021 — a <c>[EdictSagaTimeout("...")]</c> duration combined with
/// <c>Unbounded = true</c> is self-contradictory: one declares a finite cap, the
/// other declares no cap at all. The runtime resolves the conflict silently in
/// favour of unbounded, so the rule refuses the build instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SagaTimeoutUnboundedExclusivityAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT021",
        title: "EdictSagaTimeout duration and Unbounded are mutually exclusive",
        messageFormat: "[EdictSagaTimeout] sets both a duration and Unbounded = true; declare a finite cap or Unbounded, never both",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
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

        var hasDuration = attribute.ConstructorArguments.Length > 0
                          && attribute.ConstructorArguments[0].Value is string;

        var unbounded = attribute.NamedArguments
            .Any(argument => argument.Key == "Unbounded" && argument.Value.Value is true);

        if (!hasDuration || !unbounded)
        {
            return;
        }

        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                       ?? type.Locations[0];

        context.ReportDiagnostic(Diagnostic.Create(Rule, location));
    }
}
