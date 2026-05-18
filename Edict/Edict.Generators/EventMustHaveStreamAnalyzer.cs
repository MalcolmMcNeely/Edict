using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventMustHaveStreamAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT008",
        title: "Concrete Event must declare [EdictStream]",
        messageFormat: "'{0}' derives from EdictEvent and must be decorated with [EdictStream(name)]; omitting it causes silent stream misrouting",
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

        var hasStream = type.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                      == EdictWellKnownNames.EdictStreamAttributeFqn);

        if (!hasStream)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, type.Locations[0], type.Name));
        }
    }

    private static bool DerivesFromEvent(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == EdictWellKnownNames.EdictEventFqn)
            {
                return true;
            }
        }

        return false;
    }
}
