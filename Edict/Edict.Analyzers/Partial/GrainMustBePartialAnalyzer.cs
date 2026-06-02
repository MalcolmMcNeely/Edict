using System.Collections.Immutable;
using System.Linq;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Partial;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GrainMustBePartialAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor CommandHandlerRule = new DiagnosticDescriptor(
        id: "EDICT001",
        title: "Aggregate grain must be declared partial",
        messageFormat: "'{0}' derives from EdictCommandHandler and must be declared partial",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ProjectionBuilderRule = new DiagnosticDescriptor(
        id: "EDICT001",
        title: "Projection Builder grain must be declared partial",
        messageFormat: "'{0}' derives from EdictProjectionBuilder and must be declared partial",
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

    static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind != TypeKind.Class)
        {
            return;
        }

        var isCommandHandler = DerivesFrom(type, EdictWellKnownNames.EdictCommandHandlerFqn);
        var isProjectionBuilder = !isCommandHandler && DerivesFrom(type, EdictWellKnownNames.EdictProjectionBuilderFqn);

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

    static bool DerivesFrom(INamedTypeSymbol type, string baseFqn)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
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
