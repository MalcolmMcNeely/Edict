using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Generators.Commands;

internal static class SendInterceptorDiscovery
{
    /// <summary>
    /// Cheap syntactic filter — match any <c>x.SendAsync(arg)</c> invocation with
    /// exactly one argument. The semantic walk below confirms the receiver is
    /// <see cref="EdictWellKnownNames.IEdictSenderFqn"/> and the argument is a
    /// concrete <c>EdictCommand</c>.
    /// </summary>
    public static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            ArgumentList.Arguments.Count: 1,
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SendAsync" }
        };

    public static SendInvocationModel? MapInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        if (method.Name != "SendAsync")
        {
            return null;
        }

        // The receiver type (the resolved containing type the method belongs to,
        // after extension-method resolution) must be IEdictSender.
        var containing = method.ContainingType;
        if (containing is null)
        {
            return null;
        }

        if (containing.ToDisplayString(FullyQualified) != EdictWellKnownNames.IEdictSenderFqn)
        {
            return null;
        }

        if (method.Parameters.Length != 1)
        {
            return null;
        }

        var argType = model.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression, cancellationToken).Type as INamedTypeSymbol;
        if (argType is null || argType.IsAbstract)
        {
            return null;
        }

        // Must derive from EdictCommand — the concrete-typed-only rule is the
        // analyzer's job, here we just skip the call site to keep emission safe.
        if (!DerivesFromCommand(argType))
        {
            return null;
        }

        var location = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetInterceptableLocation(model, invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        return new SendInvocationModel(
            CommandFqn: argType.ToDisplayString(FullyQualified),
            LocationVersion: location.Version,
            LocationData: location.Data,
            DisplayLocation: location.GetDisplayLocation());
    }

    static bool DerivesFromCommand(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictCommandFqn)
            {
                return true;
            }
        }
        return false;
    }

    static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat;
}
