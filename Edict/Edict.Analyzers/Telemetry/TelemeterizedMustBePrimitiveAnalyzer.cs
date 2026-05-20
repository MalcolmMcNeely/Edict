using System.Collections.Immutable;
using System.Linq;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Telemetry;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TelemeterizedMustBePrimitiveAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT005",
        title: "[EdictTelemeterized] property must be a primitive type",
        messageFormat: "[EdictTelemeterized] property '{0}' must be a primitive type (bool, byte, sbyte, char, short, ushort, int, uint, long, ulong, float, double, decimal, string, or Guid)",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.Property);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;

        var hasTelemeterized = property.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                      == EdictWellKnownNames.EdictTelemeterizedAttributeFqn);

        if (!hasTelemeterized)
        {
            return;
        }

        if (IsPrimitiveType(property.Type))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, property.Locations[0], property.Name));
    }

    private static bool IsPrimitiveType(ITypeSymbol type) =>
        type.SpecialType is
            SpecialType.System_String or
            SpecialType.System_Boolean or
            SpecialType.System_Byte or
            SpecialType.System_SByte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal or
            SpecialType.System_Char
        || type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid";
}
