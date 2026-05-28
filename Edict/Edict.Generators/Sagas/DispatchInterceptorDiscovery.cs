using System.Threading;

using Edict.Generators.Commands;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Generators.Sagas;

internal static class DispatchInterceptorDiscovery
{
    /// <summary>
    /// Cheap syntactic filter — match any <c>Dispatch(arg)</c> or
    /// <c>this.Dispatch(arg)</c> invocation with one argument. The semantic
    /// walk confirms the containing type derives from
    /// <see cref="EdictWellKnownNames.EdictSagaFqn"/> and the argument is a
    /// concrete <c>EdictCommand</c>.
    /// </summary>
    public static bool IsCandidate(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } invocation)
        {
            return false;
        }

        return invocation.Expression switch
        {
            IdentifierNameSyntax { Identifier.ValueText: "Dispatch" } => true,
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Dispatch" } => true,
            _ => false,
        };
    }

    public static DispatchInvocationModel? MapInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        if (method.Name != "Dispatch" || method.Parameters.Length != 1)
        {
            return null;
        }

        var containing = method.ContainingType;
        if (containing is null || !DerivesFromSaga(containing))
        {
            return null;
        }

        var argType = model.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression, cancellationToken).Type as INamedTypeSymbol;
        if (argType is null || argType.IsAbstract)
        {
            return null;
        }

        if (!DerivesFromCommand(argType))
        {
            return null;
        }

        var location = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetInterceptableLocation(model, invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        return new DispatchInvocationModel(
            CommandFqn: argType.ToDisplayString(FullyQualified),
            LocationVersion: location.Version,
            LocationData: location.Data,
            DisplayLocation: location.GetDisplayLocation());
    }

    static bool DerivesFromSaga(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var fqn = current.IsGenericType
                ? current.OriginalDefinition.ToDisplayString(FullyQualifiedNoGenerics)
                : current.ToDisplayString(FullyQualified);
            if (fqn == EdictWellKnownNames.EdictSagaFqn)
            {
                return true;
            }
        }
        return false;
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

    static readonly SymbolDisplayFormat FullyQualifiedNoGenerics =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);
}
