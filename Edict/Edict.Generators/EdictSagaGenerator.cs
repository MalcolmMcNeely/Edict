using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Edict.Generators;

/// <summary>
/// Emits the saga spine for every <c>partial</c> grain deriving from
/// <c>Edict.Core.Sagas.EdictSaga&lt;TProgress&gt;</c>: the Orleans grain
/// interface (rooted at <c>IEdictSaga</c>), one
/// <c>[ImplicitStreamSubscription]</c> per unique stream across the grain's
/// <c>Handle(TEvent)</c> overloads, and a <c>DispatchAsync</c> type-switch with
/// per-event handler spans. Stream wiring is pure-implicit and lives
/// on <c>EdictIdempotencyBase</c> (the implicit-subscription guide's canonical
/// shape — the hybrid pattern broke referenced-assembly memory-stream delivery
/// in #53). Mirrors <see cref="EdictProjectionGenerator"/>; the only emitted
/// difference is the grain-interface root.
/// <para>
/// This generator references no Edict assembly. It matches Edict's base type
/// and annotations purely by fully-qualified name; the generic base is matched
/// via a generics-stripped base-chain walk (mirrors the command generator).
/// </para>
/// </summary>
[Generator]
public sealed class EdictSagaGenerator : IIncrementalGenerator
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
            spc.AddSource($"{grain.Namespace}.{grain.GrainName}.Saga.g.cs",
                SourceText.From(EmitGrain(grain), Encoding.UTF8)));
    }

    private static GrainModel? Map(ClassDeclarationSyntax syntax, SemanticModel model)
    {
        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol grain)
            return null;

        if (!DerivesFromSaga(grain))
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

    // Matches a consumer's saga base EdictSaga<TProgress>. Mirrors the command
    // generator's OriginalDefinition base-chain walk with a generics-stripped
    // FQN, keeping the single well-known-name comparison.
    private static bool DerivesFromSaga(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.OriginalDefinition.ToDisplayString(FullyQualifiedNoGenerics)
                == EdictWellKnownNames.EdictSagaFqn)
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

        var dispatchArms = new StringBuilder();
        foreach (var handler in grain.Handlers)
        {
            dispatchArms
                .Append("                case ").Append(handler.EventFqn).Append(" typed:\n")
                .Append("                {\n")
                .Append("                    var parentContext = ")
                .Append(EdictWellKnownNames.ActivityExtensionsFqn)
                .Append(".RestoreFromStrings(evt.TraceId, evt.SpanId, evt.TraceState);\n")
                .Append("                    using var span = ")
                .Append(EdictWellKnownNames.ActivitySourceExtensionsFqn)
                .Append(".StartEdictEventHandle(")
                .Append(EdictWellKnownNames.EdictDiagnosticsActivitySourceFqn)
                .Append(", \"").Append(handler.EventSimpleName).Append("\", parentContext);\n")
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
                public partial interface {{interfaceName}} : {{EdictWellKnownNames.IEdictSagaFqn}}
                {
                }

            {{subscriptionAttrs.ToString().TrimEnd('\n')}}
                public partial class {{grain.GrainName}} : {{interfaceName}}
                {
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

    private static readonly SymbolDisplayFormat FullyQualifiedNoGenerics =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);

    private sealed record GrainModel(
        string Namespace,
        string GrainName,
        ImmutableArray<HandlerModel> Handlers);

    private sealed record HandlerModel(
        string EventFqn,
        string EventSimpleName,
        string StreamName);
}
