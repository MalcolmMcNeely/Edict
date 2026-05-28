using System.Threading;

using Edict.Generators.Commands;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Generators.Events;

internal static class RaiseInterceptorDiscovery
{
    /// <summary>
    /// Cheap syntactic filter — match any <c>Raise(arg)</c> or
    /// <c>this.Raise(arg)</c> invocation with one argument. The semantic walk
    /// confirms the containing type derives from
    /// <see cref="EdictWellKnownNames.EdictCommandHandlerFqn"/> and the
    /// argument is a concrete <c>EdictEvent</c>.
    /// </summary>
    public static bool IsCandidate(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } invocation)
        {
            return false;
        }

        return invocation.Expression switch
        {
            IdentifierNameSyntax { Identifier.ValueText: "Raise" } => true,
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Raise" } => true,
            _ => false,
        };
    }

    public static RaiseInvocationModel? MapInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        if (method.Name != "Raise" || method.Parameters.Length != 1)
        {
            return null;
        }

        var containing = method.ContainingType;
        if (containing is null || !DerivesFromCommandHandler(containing))
        {
            return null;
        }

        var argType = model.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression, cancellationToken).Type as INamedTypeSymbol;
        if (argType is null || argType.IsAbstract)
        {
            return null;
        }

        if (!DerivesFromEvent(argType))
        {
            return null;
        }

        var location = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetInterceptableLocation(model, invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        return new RaiseInvocationModel(
            EventFqn: argType.ToDisplayString(FullyQualified),
            LocationVersion: location.Version,
            LocationData: location.Data,
            DisplayLocation: location.GetDisplayLocation());
    }

    static bool DerivesFromCommandHandler(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var fqn = current.IsGenericType
                ? current.OriginalDefinition.ToDisplayString(FullyQualifiedNoGenerics)
                : current.ToDisplayString(FullyQualified);
            if (fqn == EdictWellKnownNames.EdictCommandHandlerFqn)
            {
                return true;
            }
        }
        return false;
    }

    static bool DerivesFromEvent(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictEventFqn)
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
