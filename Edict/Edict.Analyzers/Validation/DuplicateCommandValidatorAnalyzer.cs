using System.Collections.Generic;
using System.Collections.Immutable;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Validation;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateCommandValidatorAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "EDICT019",
        title: "Command validated by multiple validators",
        messageFormat: "'{0}' is already validated by '{1}'; each command must have at most one validator",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(Analyze);
    }

    static void Analyze(CompilationAnalysisContext context)
    {
        var firstValidator = new Dictionary<string, string>();

        foreach (var type in GetAllTypes(context.Compilation.GlobalNamespace))
        {
            var validatedCommand = ResolveValidatedCommand(type);
            if (validatedCommand is null)
            {
                continue;
            }

            var commandFqn = validatedCommand.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (firstValidator.TryGetValue(commandFqn, out var existingValidator))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, type.Locations[0],
                        validatedCommand.Name, existingValidator));
            }
            else
            {
                firstValidator[commandFqn] = type.Name;
            }
        }
    }

    static INamedTypeSymbol? ResolveValidatedCommand(INamedTypeSymbol type)
    {
        var baseType = type.BaseType;
        if (baseType is null || !baseType.IsGenericType)
        {
            return null;
        }

        var openGenericFqn = baseType.OriginalDefinition.ToDisplayString(FullyQualifiedNoGenerics);
        if (openGenericFqn != EdictWellKnownNames.EdictCommandValidatorFqn)
        {
            return null;
        }

        if (baseType.TypeArguments.Length != 1)
        {
            return null;
        }

        return baseType.TypeArguments[0] as INamedTypeSymbol;
    }

    static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            yield return type;
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in GetAllTypes(childNamespace))
            {
                yield return type;
            }
        }
    }

    static readonly SymbolDisplayFormat FullyQualifiedNoGenerics =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);
}
