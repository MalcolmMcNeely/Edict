using System.Collections.Generic;
using System.Linq;

using Edict.Generators.Classification;
using Edict.Generators.Commands;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Generators.Sagas;

internal static class SagaDiscovery
{
    public static SagaGrainModel? MapSaga(ClassDeclarationSyntax syntax, SemanticModel model)
    {
        if (EdictTypeClassifier.Classify(syntax, model) != EdictTypeKind.Saga)
        {
            return null;
        }

        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol grain)
        {
            return null;
        }

        var handlers = new List<SagaHandlerModel>();
        var scheduleMessages = new List<ScheduleMessageModel>();
        var scheduleTimeoutMessages = new List<ScheduleMessageModel>();

        foreach (var method in grain.GetMembers(EdictWellKnownNames.OnScheduleTimeoutMethodName).OfType<IMethodSymbol>())
        {
            if (method.Parameters.Length != 1
                || method.ReturnType.ToDisplayString(FullyQualified) != EdictWellKnownNames.TaskFqn)
            {
                continue;
            }

            if (method.Parameters[0].Type is not INamedTypeSymbol parameterType || !DerivesFromScheduleMessage(parameterType))
            {
                continue;
            }

            AddScheduleMessage(scheduleTimeoutMessages, parameterType);
        }

        foreach (var method in grain.GetMembers(EdictWellKnownNames.HandleMethodName).OfType<IMethodSymbol>())
        {
            if (method.Parameters.Length != 1)
            {
                continue;
            }

            var parameterType = method.Parameters[0].Type as INamedTypeSymbol;
            var returnType = method.ReturnType.ToDisplayString(FullyQualified);

            if (returnType == EdictWellKnownNames.TaskFqn)
            {
                if (parameterType is null || !DerivesFromEvent(parameterType))
                {
                    continue;
                }

                var streamName = GetStreamName(parameterType);
                if (streamName is null)
                {
                    continue;
                }

                var handler = new SagaHandlerModel(
                    parameterType.ToDisplayString(FullyQualified),
                    parameterType.Name,
                    streamName);

                if (handlers.All(h => h.EventFqn != handler.EventFqn))
                {
                    handlers.Add(handler);
                }
            }
            else if (returnType == EdictWellKnownNames.TaskOfEdictScheduleResultFqn)
            {
                if (parameterType is null || !DerivesFromScheduleMessage(parameterType))
                {
                    continue;
                }

                AddScheduleMessage(scheduleMessages, parameterType);
            }
        }

        if (handlers.Count == 0)
        {
            return null;
        }

        handlers.Sort(static (a, b) => System.StringComparer.Ordinal.Compare(a.EventSimpleName, b.EventSimpleName));
        scheduleMessages.Sort(static (a, b) => string.CompareOrdinal(a.SimpleName, b.SimpleName));
        scheduleTimeoutMessages.Sort(static (a, b) => string.CompareOrdinal(a.SimpleName, b.SimpleName));

        var grainNamespace = grain.ContainingNamespace.IsGlobalNamespace
            ? "Edict.Generated"
            : grain.ContainingNamespace.ToDisplayString();

        return new SagaGrainModel(
            grainNamespace,
            grain.Name,
            new EquatableArray<SagaHandlerModel>(handlers),
            new EquatableArray<ScheduleMessageModel>(scheduleMessages),
            new EquatableArray<ScheduleMessageModel>(scheduleTimeoutMessages));
    }

    static string? GetStreamName(INamedTypeSymbol eventType)
    {
        var attr = eventType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictStreamAttributeFqn);

        return attr?.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value as string
            : null;
    }

    static void AddScheduleMessage(List<ScheduleMessageModel> messages, INamedTypeSymbol messageType)
    {
        var fqn = messageType.ToDisplayString(FullyQualified);
        if (messages.All(m => m.Fqn != fqn))
        {
            var messageNamespace = messageType.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : messageType.ContainingNamespace.ToDisplayString();
            messages.Add(new ScheduleMessageModel(fqn, messageType.Name, messageNamespace));
        }
    }

    static bool DerivesFromScheduleMessage(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictScheduleMessageFqn)
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
}
