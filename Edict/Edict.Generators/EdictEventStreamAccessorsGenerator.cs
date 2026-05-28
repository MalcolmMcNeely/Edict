using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Edict.Generators;

/// <summary>
/// Emits the per-assembly <c>EdictEventStreamRegistrar</c> plus the
/// <c>[assembly: EdictEventStreams]</c> annotation that the hand-authored
/// <c>AddEdict()</c> in Edict.Core walks at startup to stitch a runtime
/// <c>IEventStreamAccessors</c> map. Every concrete <c>EdictEvent</c> subclass
/// declared <c>partial</c> with <c>[EdictStream]</c> and exactly one
/// <c>[EdictRouteKey] Guid</c> property contributes one entry; events that fail
/// either contract are skipped here and surfaced by the analyzer pipeline
/// (EDICT003 / EDICT008) at compile time.
/// <para>
/// Removes per-publish reflection on the hot path — call sites that previously
/// went through <c>Attribute.GetCustomAttribute</c> / <c>type.GetProperties</c>
/// / <c>PropertyInfo.GetValue</c> now resolve via one dictionary lookup and a
/// compiled accessor delegate.
/// </para>
/// </summary>
[Generator]
public sealed class EdictEventStreamAccessorsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var events = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is RecordDeclarationSyntax { BaseList: not null } candidate
                    && candidate.Modifiers.Any(static m => m.ValueText == "partial")
                    && !candidate.Modifiers.Any(static m => m.ValueText == "abstract"),
                static (ctx, _) => Map((RecordDeclarationSyntax)ctx.Node, ctx.SemanticModel))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(events.Collect(), static (spc, allEvents) =>
        {
            if (allEvents.Length == 0)
            {
                return;
            }

            spc.AddSource("Edict.Generated.EdictEventStreamRegistrar.g.cs",
                SourceText.From(EmitRegistrar(allEvents), Encoding.UTF8));
        });
    }

    private static EventModel? Map(RecordDeclarationSyntax syntax, SemanticModel model)
    {
        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol eventType)
        {
            return null;
        }

        if (!DerivesFromEvent(eventType))
        {
            return null;
        }

        // file-scoped types carry a compiler-synthesised name suffix and are
        // not bindable from another assembly by their declared FQN — emitting
        // a `(OrderPlacedEvent)evt` cast against the visible name would not
        // compile. Skip silently; the framework would never route events
        // through a file-local type in production.
        if (eventType.IsFileLocal)
        {
            return null;
        }

        // The registrar lives in the `Edict.Generated` namespace of the same
        // assembly — it can see types at Internal or Public accessibility, but
        // a private/protected nested record (often used by xUnit fixtures) is
        // unreachable. Skip; production events are never declared private.
        for (INamedTypeSymbol? t = eventType; t is not null; t = t.ContainingType)
        {
            var a = t.DeclaredAccessibility;
            if (a != Accessibility.Public && a != Accessibility.Internal)
            {
                return null;
            }
        }

        var streamAttr = eventType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString(FullyQualified)
                == EdictWellKnownNames.EdictStreamAttributeFqn);

        if (streamAttr is null || streamAttr.ConstructorArguments.Length == 0)
        {
            return null;
        }

        var streamName = streamAttr.ConstructorArguments[0].Value as string;
        if (streamName is null)
        {
            return null;
        }

        string? routeKeyProperty = null;
        var routeKeyCount = 0;
        for (INamedTypeSymbol? type = eventType; type is not null; type = type.BaseType)
        {
            if (type.ToDisplayString(FullyQualified) is "global::System.Object")
            {
                break;
            }

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                var attributes = property.GetAttributes();
                if (attributes.Any(a => a.AttributeClass?.ToDisplayString(FullyQualified)
                        == EdictWellKnownNames.EdictRouteKeyAttributeFqn))
                {
                    routeKeyCount++;
                    if (routeKeyProperty is null && property.Type.ToDisplayString(FullyQualified) == "global::System.Guid")
                    {
                        routeKeyProperty = property.Name;
                    }
                }
            }
        }

        if (routeKeyProperty is null || routeKeyCount != 1)
        {
            return null;
        }

        return new EventModel(
            eventType.ToDisplayString(FullyQualified),
            streamName,
            routeKeyProperty);
    }

    private static bool DerivesFromEvent(INamedTypeSymbol type)
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

    private static string EmitRegistrar(ImmutableArray<EventModel> events)
    {
        var ordered = events
            .OrderBy(e => e.Fqn, System.StringComparer.Ordinal)
            .ToArray();

        var entries = new StringBuilder();
        foreach (var evt in ordered)
        {
            entries.Append("            accessors[typeof(")
                .Append(evt.Fqn)
                .Append(")] = new global::Edict.Contracts.Routing.EdictEventStreamAccessor(\"")
                .Append(evt.StreamName)
                .Append("\", static evt => ((")
                .Append(evt.Fqn)
                .Append(")evt).")
                .Append(evt.RouteKeyProperty)
                .Append(");\n");
        }

        return $$"""
            // <auto-generated/>
            #nullable enable

            [assembly: global::Edict.Contracts.Routing.EdictEventStreamsAttribute(typeof(global::Edict.Generated.EdictEventStreamRegistrar))]

            namespace Edict.Generated
            {
                internal static class EdictEventStreamRegistrar
                {
                    public static void Register(
                        global::System.Collections.Generic.Dictionary<
                            global::System.Type, global::Edict.Contracts.Routing.EdictEventStreamAccessor> accessors)
                    {
            {{entries.ToString().TrimEnd('\n')}}
                    }
                }
            }

            """;
    }

    private static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat;

    private sealed record EventModel(string Fqn, string StreamName, string RouteKeyProperty);
}
