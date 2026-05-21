using System.Collections.Immutable;
using System.Linq;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Persistence;

/// <summary>
/// EDICT011 — enforces the consumer-owned half of the attribute-placement
/// policy on every <c>IEdictPersistedState</c> implementer. The generator owns
/// <c>[Alias]</c> + <c>[MessagePackObject(true)]</c> on commands and events
/// (values safe to recompute every build from current syntax); the consumer owns
/// <c>[GenerateSerializer]</c> + frozen-literal <c>[Alias]</c> + per-property
/// <c>[Id(n)]</c> on persisted state (values that must survive a class rename).
/// Four sub-descriptors share id <c>EDICT011</c>:
/// <list type="bullet">
///   <item><description><c>MissingGenerateSerializer</c></description></item>
///   <item><description><c>MissingAlias</c></description></item>
///   <item><description><c>AliasNotStringLiteral</c> — the <c>nameof(T)</c> defeat the literal-only rule exists to catch</description></item>
///   <item><description><c>PropertyMissingId</c> — scoped to *declared* public instance properties only; inherited properties do not fire</description></item>
/// </list>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PersistedStateContractAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EDICT011";

    internal static readonly DiagnosticDescriptor MissingGenerateSerializerRule = new(
        id: DiagnosticId,
        title: "Persisted state must carry [GenerateSerializer]",
        messageFormat: "'{0}' implements IEdictPersistedState and must carry [GenerateSerializer]",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MissingAliasRule = new(
        id: DiagnosticId,
        title: "Persisted state must carry [Alias]",
        messageFormat: "'{0}' implements IEdictPersistedState and must carry [Alias(\"literal\")]",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor AliasNotStringLiteralRule = new(
        id: DiagnosticId,
        title: "Persisted state [Alias] argument must be a string literal",
        messageFormat: "'{0}' has an [Alias] argument that is not a string literal (a nameof(...) value defeats the rename-survival guard — write the literal directly)",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor PropertyMissingIdRule = new(
        id: DiagnosticId,
        title: "Persisted state property must carry [Id(n)]",
        messageFormat: "Property '{0}' on persisted state type '{1}' must carry [Id(n)]",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            MissingGenerateSerializerRule,
            MissingAliasRule,
            AliasNotStringLiteralRule,
            PropertyMissingIdRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // The framework's EdictUnit shim implements the marker as the empty
        // payload for stateless shims; it rides MessagePack, not the Orleans
        // codec, so EDICT011's consumer-attribute checks do not apply.
        if (type.TypeKind != TypeKind.Class)
        {
            return;
        }

        if (!ImplementsPersistedState(type))
        {
            return;
        }

        var attributes = type.GetAttributes();

        if (!attributes.Any(a => MatchesAttribute(a, EdictWellKnownNames.OrleansGenerateSerializerAttributeFqn)))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(MissingGenerateSerializerRule, type.Locations[0], type.Name));
        }

        var aliasAttribute = attributes.FirstOrDefault(a =>
            MatchesAttribute(a, EdictWellKnownNames.OrleansAliasAttributeFqn));

        if (aliasAttribute is null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(MissingAliasRule, type.Locations[0], type.Name));
        }
        else if (!IsStringLiteralArgument(aliasAttribute))
        {
            // Locate the [Alias(...)] application syntax so the diagnostic
            // highlights the consumer's argument, not the type name (the
            // nameof(...) defeat lives in argument syntax — point at it).
            var location = aliasAttribute.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                ?? type.Locations[0];
            context.ReportDiagnostic(
                Diagnostic.Create(AliasNotStringLiteralRule, location, type.Name));
        }

        foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic || member.IsIndexer || member.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            // Scope is *declared* public instance properties on this type only;
            // a property inherited from a base does not fire.
            if (!SymbolEqualityComparer.Default.Equals(member.ContainingType, type))
            {
                continue;
            }

            var memberAttributes = member.GetAttributes();
            if (memberAttributes.Any(a => MatchesAttribute(a, EdictWellKnownNames.OrleansIdAttributeFqn)))
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(PropertyMissingIdRule, member.Locations[0], member.Name, type.Name));
        }
    }

    static bool ImplementsPersistedState(INamedTypeSymbol type) =>
        type.AllInterfaces.Any(i =>
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                == EdictWellKnownNames.IEdictPersistedStateFqn);

    static bool MatchesAttribute(AttributeData attribute, string fqn) =>
        attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fqn;

    static bool IsStringLiteralArgument(AttributeData aliasAttribute)
    {
        // The literal-only rule is the analyzer's main contribution: a nameof(T)
        // argument satisfies the attribute presence check (it produces a string)
        // but breaks the frozen-literal guarantee the moment T is renamed.
        if (aliasAttribute.ConstructorArguments.Length == 0)
        {
            return false;
        }

        if (aliasAttribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax)
        {
            return false;
        }

        var arg = syntax.ArgumentList?.Arguments.FirstOrDefault();
        if (arg is null)
        {
            return false;
        }

        return arg.Expression.IsKind(SyntaxKind.StringLiteralExpression);
    }
}
