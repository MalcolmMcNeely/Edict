using System.Collections.Immutable;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Edict.Analyzers.Handlers;

/// <summary>
/// EDICT018 — flags a Task-returning method literally named <c>Handle</c> on a
/// class deriving from one of the four Edict consumer bases (Command Handler,
/// Event Handler, Saga, Projection Builder). The source generator discovers
/// consumer handlers by exact method name; once the discovery name flipped to
/// <c>HandleAsync</c>, a method still named <c>Handle</c> compiles cleanly but
/// silently never fires at runtime — this analyzer turns the silent no-op into
/// a build error. Deliberately string-matches the literal <c>"Handle"</c> rather
/// than referencing <see cref="EdictWellKnownNames.HandleMethodName"/>, because
/// the rule's job is to detect the old name even after the constant has flipped.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandleMustBeHandleAsyncAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EDICT018";

    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: DiagnosticId,
        title: "Edict handler methods must be named 'HandleAsync'",
        messageFormat: "Edict handler methods must be named 'HandleAsync', not 'Handle'. The source generator discovers handlers by name; a method named 'Handle' will compile but never fire at runtime.",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.Method);
    }

    static void Analyze(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        if (method.Name != "Handle")
        {
            return;
        }

        if (!ReturnsTask(method.ReturnType))
        {
            return;
        }

        var containingType = method.ContainingType;
        if (containingType is null || !DerivesFromEdictConsumerBase(containingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, method.Locations[0]));
    }

    static bool ReturnsTask(ITypeSymbol returnType)
    {
        if (returnType is not INamedTypeSymbol named)
        {
            return false;
        }

        var fqn = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fqn == EdictWellKnownNames.TaskFqn
            || fqn == "global::System.Threading.Tasks.Task<TResult>";
    }

    static bool DerivesFromEdictConsumerBase(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            var fqn = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var genericsStrippedFqn = current.IsGenericType
                ? current.OriginalDefinition.ToDisplayString(FullyQualifiedNoGenerics)
                : fqn;

            if (genericsStrippedFqn == EdictWellKnownNames.EdictCommandHandlerFqn
                || genericsStrippedFqn == EdictWellKnownNames.EdictEventHandlerFqn
                || genericsStrippedFqn == EdictWellKnownNames.EdictSagaFqn
                || genericsStrippedFqn == EdictWellKnownNames.EdictProjectionBuilderFqn)
            {
                return true;
            }
        }

        return false;
    }

    static readonly SymbolDisplayFormat FullyQualifiedNoGenerics =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);
}
