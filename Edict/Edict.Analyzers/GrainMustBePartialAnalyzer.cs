using System.Collections.Immutable;
using System.Linq;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GrainMustBePartialAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor CommandHandlerRule = new DiagnosticDescriptor(
        id: "EDICT001",
        title: "Aggregate grain must be declared partial",
        messageFormat: "'{0}' derives from EdictCommandHandlerGrain and must be declared partial",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ProjectionBuilderRule = new DiagnosticDescriptor(
        id: "EDICT001",
        title: "Projection Builder grain must be declared partial",
        messageFormat: "'{0}' derives from EdictProjectionBuilderGrain and must be declared partial",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(CommandHandlerRule, ProjectionBuilderRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind != TypeKind.Class)
        {
            return;
        }

        var isCommandHandler = type.BaseType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == EdictWellKnownNames.EdictCommandHandlerGrainFqn;
        var isProjectionBuilder = !isCommandHandler && DerivesFrom(type, EdictWellKnownNames.EdictProjectionBuilderGrainFqn);

        if (!isCommandHandler && !isProjectionBuilder)
        {
            return;
        }

        var isPartial = type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(s => s.Modifiers.Any(m => m.ValueText == "partial"));

        if (!isPartial)
        {
            var rule = isCommandHandler ? CommandHandlerRule : ProjectionBuilderRule;
            context.ReportDiagnostic(Diagnostic.Create(rule, type.Locations[0], type.Name));
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
}
