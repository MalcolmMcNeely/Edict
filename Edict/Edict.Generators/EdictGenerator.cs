using System.Linq;
using System.Text;

using Edict.Generators.Classification;
using Edict.Generators.Commands;
using Edict.Generators.EventHandler;
using Edict.Generators.Events;
using Edict.Generators.EventStreamAccessors;
using Edict.Generators.EventTagWriters;
using Edict.Generators.Projections;
using Edict.Generators.Sagas;
using Edict.Generators.Shared;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Edict.Generators;

/// <summary>
/// The single Edict source generator. Walks partial class and record
/// declarations, classifies each via <see cref="EdictTypeClassifier"/>, and
/// dispatches to the per-concept discovery + emitter modules under
/// <c>Edict.Generators.Commands</c>, <c>.Events</c>, <c>.EventHandler</c>,
/// <c>.EventStreamAccessors</c>, <c>.Projections</c>, and <c>.Sagas</c>.
/// <para>
/// Replaces the previous one-generator-per-concept layout. ADR-0005 still
/// holds — no assembly reference; bases and annotations are matched purely by
/// fully-qualified name. The per-concept Verify snapshots in
/// <c>Edict.Generators.Tests</c> are the regression net.
/// </para>
/// </summary>
[Generator]
public sealed class EdictGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Commands ────────────────────────────────────────────────────────────
        var commandRecords = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is RecordDeclarationSyntax { BaseList: not null } candidate
                    && candidate.Modifiers.Any(static m => m.ValueText == "partial")
                    && !candidate.Modifiers.Any(static m => m.ValueText == "abstract"),
                static (ctx, _) => CommandDiscovery.MapCommandRecord((RecordDeclarationSyntax)ctx.Node, ctx.SemanticModel))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        var commandHandlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } candidate
                    && candidate.Modifiers.Any(static m => m.ValueText == "partial"),
                static (ctx, _) => CommandDiscovery.MapCommandHandler((ClassDeclarationSyntax)ctx.Node, ctx.SemanticModel))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(commandHandlers, static (spc, grain) =>
            spc.AddSource($"{grain.Namespace}.{grain.GrainName}.g.cs",
                SourceText.From(CommandGrainSpineEmitter.Emit(grain), Encoding.UTF8)));

        context.RegisterSourceOutput(commandRecords, static (spc, command) =>
            spc.AddSource($"{command.Namespace}.{command.SimpleName}.Alias.g.cs",
                SourceText.From(SharedAliasEmitter.Emit(command.Namespace, command.SimpleName), Encoding.UTF8)));

        context.RegisterSourceOutput(commandHandlers.Collect(), static (spc, allGrains) =>
        {
            if (allGrains.Length == 0)
            {
                return;
            }

            spc.AddSource("Edict.Generated.EdictRouteRegistrar.g.cs",
                SourceText.From(CommandRouteRegistrarEmitter.Emit(allGrains), Encoding.UTF8));
        });

        // Events ──────────────────────────────────────────────────────────────
        var eventRecords = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is RecordDeclarationSyntax { BaseList: not null } candidate
                    && candidate.Modifiers.Any(static m => m.ValueText == "partial")
                    && !candidate.Modifiers.Any(static m => m.ValueText == "abstract"),
                static (ctx, _) => EventDiscovery.MapEventRecord((RecordDeclarationSyntax)ctx.Node, ctx.SemanticModel))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(eventRecords, static (spc, edictEvent) =>
            spc.AddSource($"{edictEvent.Namespace}.{edictEvent.SimpleName}.Alias.g.cs",
                SourceText.From(SharedAliasEmitter.Emit(edictEvent.Namespace, edictEvent.SimpleName), Encoding.UTF8)));

        // EventStreamAccessors ────────────────────────────────────────────────
        var eventAccessors = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is RecordDeclarationSyntax { BaseList: not null } candidate
                    && candidate.Modifiers.Any(static m => m.ValueText == "partial")
                    && !candidate.Modifiers.Any(static m => m.ValueText == "abstract"),
                static (ctx, _) => EventStreamAccessorDiscovery.MapEventForAccessor((RecordDeclarationSyntax)ctx.Node, ctx.SemanticModel))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(eventAccessors.Collect(), static (spc, allEvents) =>
        {
            if (allEvents.Length == 0)
            {
                return;
            }

            spc.AddSource("Edict.Generated.EdictEventStreamRegistrar.g.cs",
                SourceText.From(EventStreamRegistrarEmitter.Emit(allEvents), Encoding.UTF8));
        });

        // EventTagWriters ─────────────────────────────────────────────────────
        var eventTagWriters = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is RecordDeclarationSyntax { BaseList: not null } candidate
                    && candidate.Modifiers.Any(static m => m.ValueText == "partial")
                    && !candidate.Modifiers.Any(static m => m.ValueText == "abstract"),
                static (ctx, _) => EventTagWritersDiscovery.MapEvent((RecordDeclarationSyntax)ctx.Node, ctx.SemanticModel))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(eventTagWriters.Collect(), static (spc, allEvents) =>
        {
            if (allEvents.Length == 0)
            {
                return;
            }

            spc.AddSource("Edict.Generated.EdictEventTagWritersRegistrar.g.cs",
                SourceText.From(EventTagWritersRegistrarEmitter.Emit(allEvents), Encoding.UTF8));
        });

        // EventHandler ────────────────────────────────────────────────────────
        var eventHandlerGrains = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } candidate
                    && candidate.Modifiers.Any(static m => m.ValueText == "partial"),
                static (ctx, _) => EventHandlerDiscovery.MapEventHandler((ClassDeclarationSyntax)ctx.Node, ctx.SemanticModel))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(eventHandlerGrains, static (spc, grain) =>
            spc.AddSource($"{grain.Namespace}.{grain.GrainName}.EventHandler.g.cs",
                SourceText.From(EventHandlerGrainSpineEmitter.Emit(grain), Encoding.UTF8)));

        // Projections ─────────────────────────────────────────────────────────
        var projectionGrains = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } candidate
                    && candidate.Modifiers.Any(static m => m.ValueText == "partial"),
                static (ctx, _) => ProjectionDiscovery.MapProjection((ClassDeclarationSyntax)ctx.Node, ctx.SemanticModel))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(projectionGrains, static (spc, grain) =>
            spc.AddSource($"{grain.Namespace}.{grain.GrainName}.g.cs",
                SourceText.From(ProjectionGrainSpineEmitter.Emit(grain), Encoding.UTF8)));

        // Sagas ───────────────────────────────────────────────────────────────
        var sagaGrains = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } candidate
                    && candidate.Modifiers.Any(static m => m.ValueText == "partial"),
                static (ctx, _) => SagaDiscovery.MapSaga((ClassDeclarationSyntax)ctx.Node, ctx.SemanticModel))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(sagaGrains, static (spc, grain) =>
            spc.AddSource($"{grain.Namespace}.{grain.GrainName}.Saga.g.cs",
                SourceText.From(SagaGrainSpineEmitter.Emit(grain), Encoding.UTF8)));

        // Interceptors ────────────────────────────────────────────────────────
        // Per-type intercept stubs for concrete-typed
        // IEdictSender.Send / EdictCommandHandler.Raise / EdictSaga.Dispatch
        // call sites. Generator-driven fast path; the registrars stay
        // load-bearing for deferred dispatch from persisted state.
        var interceptorsEnabled = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                !options.GlobalOptions.TryGetValue("build_property.EdictInterceptorsEnabled", out var v)
                || !string.Equals(v, "false", System.StringComparison.OrdinalIgnoreCase));

        var sendInvocations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => SendInterceptorDiscovery.IsCandidate(node),
                static (ctx, cancellationToken) => SendInterceptorDiscovery.MapInvocation(
                    (InvocationExpressionSyntax)ctx.Node, ctx.SemanticModel, cancellationToken))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        var sendInterceptorBundles = sendInvocations.Collect()
            .Combine(commandHandlers.Collect())
            .Combine(interceptorsEnabled)
            .Select(static (tuple, _) =>
            {
                var (invocationsAndGrains, enabled) = tuple;
                if (!enabled)
                {
                    return System.Collections.Immutable.ImmutableArray<SendInterceptorEmitter.TypeBundle>.Empty;
                }
                var (invocations, grains) = invocationsAndGrains;
                return SendInterceptorEmitter.Group(invocations, grains);
            });

        context.RegisterSourceOutput(sendInterceptorBundles, static (spc, bundles) =>
        {
            foreach (var bundle in bundles)
            {
                spc.AddSource(
                    $"{StripGlobal(bundle.Command.Fqn)}.SendInterceptor.g.cs",
                    SourceText.From(SendInterceptorEmitter.Emit(bundle), Encoding.UTF8));
            }
        });

        var raiseInvocations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => RaiseInterceptorDiscovery.IsCandidate(node),
                static (ctx, cancellationToken) => RaiseInterceptorDiscovery.MapInvocation(
                    (InvocationExpressionSyntax)ctx.Node, ctx.SemanticModel, cancellationToken))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        var raiseInterceptorBundles = raiseInvocations.Collect()
            .Combine(interceptorsEnabled)
            .Select(static (tuple, _) =>
            {
                var (invocations, enabled) = tuple;
                return enabled
                    ? RaiseInterceptorEmitter.Group(invocations)
                    : System.Collections.Immutable.ImmutableArray<RaiseInterceptorEmitter.TypeBundle>.Empty;
            });

        context.RegisterSourceOutput(raiseInterceptorBundles, static (spc, bundles) =>
        {
            foreach (var bundle in bundles)
            {
                spc.AddSource(
                    $"{StripGlobal(bundle.EventFqn)}.RaiseInterceptor.g.cs",
                    SourceText.From(RaiseInterceptorEmitter.Emit(bundle), Encoding.UTF8));
            }
        });

        var dispatchInvocations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => DispatchInterceptorDiscovery.IsCandidate(node),
                static (ctx, cancellationToken) => DispatchInterceptorDiscovery.MapInvocation(
                    (InvocationExpressionSyntax)ctx.Node, ctx.SemanticModel, cancellationToken))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        var dispatchInterceptorBundles = dispatchInvocations.Collect()
            .Combine(interceptorsEnabled)
            .Select(static (tuple, _) =>
            {
                var (invocations, enabled) = tuple;
                return enabled
                    ? DispatchInterceptorEmitter.Group(invocations)
                    : System.Collections.Immutable.ImmutableArray<DispatchInterceptorEmitter.TypeBundle>.Empty;
            });

        context.RegisterSourceOutput(dispatchInterceptorBundles, static (spc, bundles) =>
        {
            foreach (var bundle in bundles)
            {
                spc.AddSource(
                    $"{StripGlobal(bundle.CommandFqn)}.DispatchInterceptor.g.cs",
                    SourceText.From(DispatchInterceptorEmitter.Emit(bundle), Encoding.UTF8));
            }
        });
    }

    static string StripGlobal(string fqn) =>
        fqn.StartsWith("global::", System.StringComparison.Ordinal)
            ? fqn.Substring("global::".Length)
            : fqn;
}
