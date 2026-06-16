using System.Collections.Immutable;
using System.Linq;

using Edict.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Edict.Analyzers.Interceptors;

/// <summary>
/// EDICT024 — flags a bare origin send, the single-argument
/// <see cref="EdictWellKnownNames.IEdictSenderFqn"/>.SendAsync(command), when the
/// command's route-key type carries <c>[EdictTenantScoped]</c>, so a tenant-scoped
/// command that would route into the default key space is caught at compile time
/// rather than as the runtime fail-closed throw. On by default and selective: the
/// marker is static source, so a public aggregate never trips it. The explicit
/// SendAsync(command, tenant) establishing-crossing overload supplies the tenant
/// and is not flagged; a site backed by a registered AddEdictTenant resolver
/// suppresses with SuppressMessage. Framework-relayed sends (saga Dispatch, outbox
/// SendCommand, schedule fire) are distinct methods a consumer never writes, so
/// they never match.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OriginSendTenantAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EDICT024";

    internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: DiagnosticId,
        title: "Origin send of a tenant-scoped command has no tenant",
        messageFormat: "'IEdictSender.SendAsync(command)' is an origin send of the tenant-scoped '{0}' with no tenant — pass it through the 'SendAsync(command, tenant)' establishing-crossing overload, or suppress this site if a registered AddEdictTenant resolver attributes it at runtime",
        category: "Edict",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

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

        if (ResolveCommandType(operation) is not { } commandType || !RouteKeyTypeIsTenantScoped(commandType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, operation.Syntax.GetLocation(), commandType.Name));
    }

    static INamedTypeSymbol? ResolveCommandType(IInvocationOperation operation)
    {
        var argument = operation.Arguments.Length == 1 ? operation.Arguments[0].Value : null;

        // Peel the implicit upcast to EdictCommand so the concrete command type the
        // call site names is what we inspect; a genuinely base-typed send carries no
        // route key on EdictCommand and falls through to no diagnostic.
        while (argument is IConversionOperation { IsImplicit: true } conversion)
        {
            argument = conversion.Operand;
        }

        return argument?.Type as INamedTypeSymbol;
    }

    static bool RouteKeyTypeIsTenantScoped(INamedTypeSymbol commandType)
    {
        for (INamedTypeSymbol? type = commandType; type is not null; type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (!property.GetAttributes().Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        == EdictWellKnownNames.EdictRouteKeyAttributeFqn))
                {
                    continue;
                }

                return property.Type.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    == EdictWellKnownNames.EdictTenantScopedAttributeFqn);
            }
        }

        return false;
    }
}
