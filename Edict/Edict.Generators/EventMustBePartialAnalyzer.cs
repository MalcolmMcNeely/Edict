using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventMustBePartialAnalyzer : DiagnosticAnalyzer
{
    private const string EventFqn = "global::Edict.Contracts.Events.Event";

    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT007",
        title: "Concrete Event must be declared partial",
        messageFormat: "'{0}' derives from Event and must be declared partial; the source generator emits the Orleans [Alias] into a second partial declaration",
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

        if (type.IsAbstract || !DerivesFromEvent(type))
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
