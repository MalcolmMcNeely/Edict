using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Edict.Generators;

/// <summary>
/// Emits the projection spine for every <c>partial</c> grain deriving from
/// <c>Edict.Core.Grains.ProjectionBuilderGrain</c>: the Orleans grain interface,
/// one <c>[ImplicitStreamSubscription]</c> per unique stream across the grain's
/// <c>Handle(TEvent)</c> overloads, a <c>SubscribeToStreamAsync</c> override,
/// and a <c>DispatchAsync</c> type-switch with per-event handler spans (ADR 0003).
/// <para>
/// ADR 0005: this generator references no Edict assembly. It matches Edict's
/// base type and annotations purely by fully-qualified name.
/// </para>
/// </summary>
[Generator]
public sealed class EdictProjectionGenerator : IIncrementalGenerator
{
    private const string ProjectionBuilderGrainFqn = "global::Edict.Core.Grains.ProjectionBuilderGrain";
    private const string EventFqn = "global::Edict.Contracts.Events.Event";
    private const string StreamAttributeFqn = "global::Edict.Contracts.Events.StreamAttribute";
    private const string TaskFqn = "global::System.Threading.Tasks.Task";

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
            spc.AddSource($"{grain.Namespace}.{grain.GrainName}.g.cs",
                SourceText.From(EmitGrain(grain), Encoding.UTF8)));
    }

    private static GrainModel? Map(ClassDeclarationSyntax syntax, SemanticModel model)
    {
        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol grain)
            return null;

        if (grain.BaseType?.ToDisplayString(FullyQualified) != ProjectionBuilderGrainFqn)
            return null;

        var handlers = new List<HandlerModel>();

        foreach (var method in grain.GetMembers("Handle").OfType<IMethodSymbol>())
        {
            if (method.Parameters.Length != 1)
                continue;

            if (method.ReturnType.ToDisplayString(FullyQualified) != TaskFqn)
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
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString(FullyQualified) == StreamAttributeFqn);

        return attr?.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value as string
            : null;
    }

    private static bool DerivesFromEvent(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(FullyQualified) == EventFqn)
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

        var subscribeBody = new StringBuilder();
        for (var i = 0; i < streamNames.Length; i++)
        {
            subscribeBody
                .Append("            var stream").Append(i)
                .Append(" = provider.GetStream<global::Edict.Contracts.Events.Event>(\n")
                .Append("                global::Orleans.Runtime.StreamId.Create(\"")
                .Append(streamNames[i])
                .Append("\", key));\n")
                .Append("            await stream").Append(i)
                .Append(".SubscribeAsync(OnStreamEventAsync, static _ => global::System.Threading.Tasks.Task.CompletedTask);\n");
        }

        var dispatchArms = new StringBuilder();
        foreach (var handler in grain.Handlers)
        {
            dispatchArms
                .Append("                case ").Append(handler.EventFqn).Append(" typed:\n")
                .Append("                {\n")
                .Append("                    var parentContext = RestoreEventContext(evt);\n")
                .Append("                    using var span = global::Edict.Core.Diagnostics.EdictDiagnostics.ActivitySource.StartActivity(\n")
                .Append("                        \"edict.event.handle ").Append(handler.EventSimpleName).Append("\",\n")
                .Append("                        global::System.Diagnostics.ActivityKind.Consumer,\n")
                .Append("                        parentContext);\n")
                .Append("                    await Handle(typed);\n")
                .Append("                    return true;\n")
                .Append("                }\n");
        }

        return $$"""
            // <auto-generated/>
            #nullable enable

            using Orleans.Streams;

            namespace {{grain.Namespace}}
            {
                /// <summary>Generated Orleans grain interface for {{grain.GrainName}}.</summary>
                public partial interface {{interfaceName}} : global::Edict.Core.Grains.IEdictProjectionBuilder
                {
                }

            {{subscriptionAttrs.ToString().TrimEnd('\n')}}
                public partial class {{grain.GrainName}} : {{interfaceName}}
                {
                    protected override async global::System.Threading.Tasks.Task SubscribeToStreamAsync(
                        global::System.Threading.CancellationToken cancellationToken)
                    {
                        var provider = this.GetStreamProvider("edict");
                        var key = this.GetPrimaryKey();
            {{subscribeBody.ToString().TrimEnd('\n')}}
                    }

                    protected override async global::System.Threading.Tasks.Task<bool> DispatchAsync(
                        global::Edict.Contracts.Events.Event evt)
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
