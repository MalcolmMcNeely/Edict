using System.Collections.Immutable;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Edict.Analyzers.Interceptors;

/// <summary>
/// EDICT023 — flags a bare origin send, the single-argument
/// <see cref="EdictWellKnownNames.IEdictSenderFqn"/>.SendAsync(command), so an
/// audit-adopting consumer sees an un-attributable send at compile time instead
/// of discovering the runtime fail-closed throw. Opt-in (off by default): an
/// analyzer cannot see whether AddEdictAudit is wired in another assembly, so it
/// flags origin sends only when a consumer turns it on through the .editorconfig
/// severity knob, making a compliance statement. The explicit
/// SendAsync(command, principal) overload is already attributed and not flagged;
/// framework-relayed sends (saga Dispatch, outbox SendCommand, schedule fire) are
/// distinct methods a consumer never writes, so they never match.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OriginSendPrincipalAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EDICT023";

    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: DiagnosticId,
        title: "Origin send has no principal",
        messageFormat: "'IEdictSender.SendAsync(command)' is an origin send with no principal — supply one with the 'SendAsync(command, principal)' overload, or suppress this site if a registered resolver attributes it at runtime",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: false);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    static void Analyze(OperationAnalysisContext context)
    {
        var operation = (IInvocationOperation)context.Operation;
        var method = operation.TargetMethod;

        if (method.Name != "SendAsync" || method.Parameters.Length != 1)
        {
            return;
        }

        var containingFqn = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (containingFqn != EdictWellKnownNames.IEdictSenderFqn)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, operation.Syntax.GetLocation()));
    }
}
