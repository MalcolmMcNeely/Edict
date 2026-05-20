using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Edict.Generators;

/// <summary>
/// Emits the event-handler spine for every <c>partial</c> grain deriving from
/// <c>Edict.Core.EventHandler.EdictEventHandler</c> (ADR 0023): the Orleans
/// grain interface, one <c>[ImplicitStreamSubscription]</c> per unique stream
/// across the grain's <c>Handle(TEvent)</c> overloads, a <c>DispatchAsync</c>
/// type-switch with per-event handler spans (ADR 0003), and — distinct from
/// projection-builder / saga emit — a synchronous <c>HandlesType</c> pre-flight
/// the stream-callback path uses to gate ring-slot consumption so an unhandled
/// event type stays a pure no-op (ADR 0023, contracts-vs-roles consequence).
/// <para>
/// Mirrors <see cref="EdictProjectionGenerator"/> shape-for-shape with the
/// additional <c>HandlesType</c> emit; the contract change is invisible to
/// existing projection-builder / saga consumers because their generators do
/// not emit <c>HandlesType</c> at all (the call site for it lives only on
/// <c>EdictEventHandler</c>'s overridden stream callback).
/// </para>
/// <para>
/// ADR 0005: this generator references no Edict assembly. It matches Edict's
/// base type and annotations purely by fully-qualified name.
/// </para>
/// </summary>
[Generator]
public sealed class EdictEventHandlerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var grains = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } candidate
                    && candidate.Modifiers.Any(static m => m.ValueText == "partial"),
                static (ctx, _) => Map((ClassDeclarationSyntax)ctx.Node, ctx.SemanticModel))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(grains, static (spc, grain) =>
            spc.AddSource($"{grain.Namespace}.{grain.GrainName}.EventHandler.g.cs",
                SourceText.From(EmitGrain(grain), Encoding.UTF8)));
    }

    private static GrainModel? Map(ClassDeclarationSyntax syntax, SemanticModel model)
    {
        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol grain)
            return null;

        if (!DerivesFromEventHandler(grain))
            return null;

        var handlers = new List<HandlerModel>();

        foreach (var method in grain.GetMembers("Handle").OfType<IMethodSymbol>())
        {
            if (method.Parameters.Length != 1)
                continue;

            if (method.ReturnType.ToDisplayString(FullyQualified) != EdictWellKnownNames.TaskFqn)
                continue;

            if (method.Parameters[0].Type is not INamedTypeSymbol eventType)
                continue;

            if (!DerivesFromEvent(eventType))
                continue;

            var streamName = GetStreamName(eventType);
            if (streamName is null)
                continue;

            var handler = new HandlerModel(
                eventType.ToDisplayString(FullyQualified),
                eventType.Name,
                streamName);

            if (handlers.All(h => h.EventFqn != handler.EventFqn))
                handlers.Add(handler);
        }

        if (handlers.Count == 0)
            return null;

        handlers.Sort(static (a, b) => System.StringComparer.Ordinal.Compare(a.EventSimpleName, b.EventSimpleName));

        var grainNamespace = grain.ContainingNamespace.IsGlobalNamespace
            ? "Edict.Generated"
            : grain.ContainingNamespace.ToDisplayString();

        return new GrainModel(grainNamespace, grain.Name, handlers.ToImmutableArray());
    }

    private static string? GetStreamName(INamedTypeSymbol eventType)
    {
        var attr = eventType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictStreamAttributeFqn);

        return attr?.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value as string
            : null;
    }

    private static bool DerivesFromEvent(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictEventFqn)
                return true;
        }
        return false;
    }

    private static bool DerivesFromEventHandler(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            var fqn = current.IsGenericType
                ? current.OriginalDefinition.ToDisplayString(FullyQualified)
                : current.ToDisplayString(FullyQualified);
            if (fqn == EdictWellKnownNames.EdictEventHandlerFqn)
                return true;
        }
        return false;
    }

    private static string EmitGrain(GrainModel grain)
    {
        var interfaceName = "I" + grain.GrainName;

        var streamNames = grain.Handlers
            .Select(h => h.StreamName)
            .Distinct(System.StringComparer.Ordinal)
            .OrderBy(n => n, System.StringComparer.Ordinal)
            .ToArray();

        var subscriptionAttrs = new StringBuilder();
        foreach (var name in streamNames)
        {
            subscriptionAttrs
                .Append("    [global::Orleans.ImplicitStreamSubscriptionAttribute(\"")
                .Append(name)
                .Append("\")]\n");
        }

        var handlesTypeArms = new StringBuilder();
        foreach (var handler in grain.Handlers)
        {
            handlesTypeArms
                .Append("                ").Append(handler.EventFqn).Append(" => true,\n");
        }

        // EdictEventHandler arms intentionally do NOT open an
        // edict.event.handle span — the InvokeHandlerExecutor already opened
        // one outer-span using the entry's captured traceparent so the
        // deferred invocation nests correctly under the publish span across
        // backoff (ADR 0003 / ADR 0023). Adding a per-arm span here would
        // double-wrap every invocation as two sibling spans under publish.
        var dispatchArms = new StringBuilder();
        foreach (var handler in grain.Handlers)
        {
            dispatchArms
                .Append("                case ").Append(handler.EventFqn).Append(" typed:\n")
                .Append("                {\n")
                .Append("                    await DispatchEventAsync(typed, Handle);\n")
                .Append("                    return true;\n")
                .Append("                }\n");
        }

        return $$"""
            // <auto-generated/>
            #nullable enable

            namespace {{grain.Namespace}}
            {
                /// <summary>Generated Orleans grain interface for {{grain.GrainName}}.</summary>
                public partial interface {{interfaceName}} : global::Edict.Core.Idempotency.IEdictEventConsumer
                {
                }

            {{subscriptionAttrs.ToString().TrimEnd('\n')}}
                public partial class {{grain.GrainName}} : {{interfaceName}}
                {
                    protected override bool HandlesType(
                        global::Edict.Contracts.Events.EdictEvent evt)
                    {
                        return evt switch
                        {
            {{handlesTypeArms.ToString().TrimEnd('\n')}}
                            _ => false,
                        };
                    }

                    protected override async global::System.Threading.Tasks.Task<bool> DispatchAsync(
                        global::Edict.Contracts.Events.EdictEvent evt)
                    {
                        switch (evt)
                        {
            {{dispatchArms.ToString().TrimEnd('\n')}}
                            default:
                                return false;
                        }
                    }
                }
            }

            """;
    }

    private static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat;

    private sealed record GrainModel(
        string Namespace,
        string GrainName,
        ImmutableArray<HandlerModel> Handlers);

    private sealed record HandlerModel(
        string EventFqn,
        string EventSimpleName,
        string StreamName);
}
