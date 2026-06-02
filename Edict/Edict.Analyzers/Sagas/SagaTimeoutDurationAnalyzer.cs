using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Sagas;

/// <summary>
/// EDICT020 — the <c>[EdictSagaTimeout("...")]</c> duration literal must parse to
/// a <c>TimeSpan</c> greater than zero. A typo or a zero/negative cap would arm a
/// reminder that fires immediately or never, mis-capping the saga silently.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SagaTimeoutDurationAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT020",
        title: "EdictSagaTimeout duration must be a positive TimeSpan literal",
        messageFormat: "'{0}' is not a positive TimeSpan-parseable duration; [EdictSagaTimeout] needs a literal such as \"24:00:00\"",
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

        if (attribute is null || attribute.ConstructorArguments.Length == 0)
        {
            return;
        }

        if (attribute.ConstructorArguments[0].Value is not string duration)
        {
            return;
        }

        if (TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out var parsed) && parsed > TimeSpan.Zero)
        {
            return;
        }

        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                       ?? type.Locations[0];

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, duration));
    }
}
