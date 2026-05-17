using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GrainMustBePartialAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT001",
        title: "Aggregate grain must be declared partial",
        messageFormat: "'{0}' derives from CommandHandlerGrain and must be declared partial",
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

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind != TypeKind.Class)
        {
            return;
        }

        if (type.BaseType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                != "global::Edict.Core.Grains.CommandHandlerGrain")
        {
            return;
        }

        var isPartial = type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(s => s.Modifiers.Any(m => m.ValueText == "partial"));

        if (!isPartial)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, type.Locations[0], type.Name));
        }
    }
}
