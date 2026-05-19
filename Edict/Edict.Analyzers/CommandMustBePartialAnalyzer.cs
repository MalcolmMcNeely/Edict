using System.Collections.Immutable;
using System.Linq;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CommandMustBePartialAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT006",
        title: "Concrete Command must be declared partial",
        messageFormat: "'{0}' derives from EdictCommand and must be declared partial; the source generator emits the Orleans [Alias] into a second partial declaration",
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

        if (type.IsAbstract || !DerivesFromCommand(type))
        {
            return;
        }

        var isPartial = type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .Any(s => s switch
            {
                ClassDeclarationSyntax cls => cls.Modifiers.Any(m => m.ValueText == "partial"),
                RecordDeclarationSyntax rec => rec.Modifiers.Any(m => m.ValueText == "partial"),
                _ => false
            });

        if (!isPartial)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, type.Locations[0], type.Name));
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
