using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Edict.Generators;

/// <summary>
/// Emits the command spine for every <c>partial</c> grain deriving from
/// <c>Edict.Core.Grains.EdictCommandHandlerGrain</c>: the Orleans grain interface, the
/// <c>Dispatch</c> type-switch override, an Orleans surrogate + converter per
/// concrete command, and a single <c>AddEdict()</c> that wires the route map,
/// sender and ActivitySource.
/// <para>
/// ADR 0005: this generator references no Edict assembly. It matches Edict's
/// base type and annotations purely by fully-qualified name.
/// </para>
/// </summary>
[Generator]
public sealed class EdictCommandGenerator : IIncrementalGenerator
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
            spc.AddSource($"{grain.Namespace}.{grain.GrainName}.g.cs",
                SourceText.From(EmitGrain(grain), Encoding.UTF8)));

        context.RegisterSourceOutput(grains, static (spc, grain) =>
        {
            foreach (var command in grain.Commands)
            {
                spc.AddSource($"{command.Namespace}.{command.SimpleName}.Alias.g.cs",
                    SourceText.From(EmitAlias(command), Encoding.UTF8));
            }
        });

        context.RegisterSourceOutput(grains.Collect(), static (spc, allGrains) =>
        {
            if (allGrains.Length == 0)
            {
                return;
            }

            spc.AddSource("Edict.Generated.AddEdict.g.cs",
                SourceText.From(EmitAddEdict(allGrains), Encoding.UTF8));
        });
    }

    private static GrainModel? Map(ClassDeclarationSyntax syntax, SemanticModel model)
    {
        if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol grain)
        {
            return null;
        }

        if (grain.BaseType?.ToDisplayString(FullyQualified) != EdictWellKnownNames.EdictCommandHandlerGrainFqn)
        {
            return null;
        }

        var commands = new List<CommandModel>();

        foreach (var method in grain.GetMembers("Handle").OfType<IMethodSymbol>())
        {
            if (method.Parameters.Length != 1)
            {
                continue;
            }

            if (method.ReturnType.ToDisplayString(FullyQualified) != EdictWellKnownNames.TaskOfEdictCommandResultFqn)
            {
                continue;
            }

            var commandType = method.Parameters[0].Type as INamedTypeSymbol;
            if (commandType is null || !DerivesFromCommand(commandType))
            {
                continue;
            }

            var command = MapCommand(commandType);
            if (command is not null && commands.All(c => c.Fqn != command.Fqn))
            {
                commands.Add(command);
            }
        }

        if (commands.Count == 0)
        {
            return null;
        }

        commands.Sort(static (a, b) => string.CompareOrdinal(a.SimpleName, b.SimpleName));

        var grainNamespace = grain.ContainingNamespace.IsGlobalNamespace
            ? "Edict.Generated"
            : grain.ContainingNamespace.ToDisplayString();

        var grainFqn = grain.ToDisplayString(FullyQualified);

        return new GrainModel(
            grainNamespace,
            grain.Name,
            // Orleans matches GetGrain's class-name prefix against the grain's
            // full type name. We can't emit a [GrainType] (Orleans' own codegen
            // is a sibling generator and never sees our generated partial), so
            // the route carries the full type name and the sender uses it to
            // disambiguate the many grain classes sharing IEdictCommandHandler.
            grainFqn.StartsWith("global::", System.StringComparison.Ordinal)
                ? grainFqn.Substring("global::".Length)
                : grainFqn,
            grainFqn,
            commands.ToImmutableArray());
    }

    private static CommandModel? MapCommand(INamedTypeSymbol command)
    {
        string? routeKeyProperty = null;
        var telemeterizedProperties = new List<TelemeterizedProperty>();

        for (INamedTypeSymbol? type = command; type is not null; type = type.BaseType)
        {
            if (type.ToDisplayString(FullyQualified) is "global::System.Object")
            {
                break;
            }

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                var attributes = property.GetAttributes();

                if (routeKeyProperty is null &&
                    attributes.Any(a => a.AttributeClass?.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictRouteKeyAttributeFqn))
                {
                    routeKeyProperty = property.Name;
                }

                if (IsPrimitiveType(property.Type) &&
                    attributes.Any(a => a.AttributeClass?.ToDisplayString(FullyQualified) == EdictWellKnownNames.EdictTelemeterizedAttributeFqn))
                {
                    telemeterizedProperties.Add(new TelemeterizedProperty(property.Name));
                }
            }
        }

        if (routeKeyProperty is null)
        {
            return null;
        }

        var commandNamespace = command.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : command.ContainingNamespace.ToDisplayString();

        return new CommandModel(
            command.ToDisplayString(FullyQualified),
            command.Name,
            commandNamespace,
            routeKeyProperty,
            telemeterizedProperties.ToImmutableArray());
    }

    private static bool IsPrimitiveType(ITypeSymbol type) =>
        type.SpecialType is
            SpecialType.System_String or
            SpecialType.System_Boolean or
            SpecialType.System_Byte or
            SpecialType.System_SByte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal or
            SpecialType.System_Char
        || type.ToDisplayString(FullyQualified) == "global::System.Guid";

    private static bool DerivesFromCommand(INamedTypeSymbol type)
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

    private static string EmitGrain(GrainModel grain)
    {
        var interfaceName = "I" + grain.GrainName;
        var arms = new StringBuilder();

        foreach (var command in grain.Commands)
        {
            arms.Append("                ")
                .Append(command.Fqn)
                .Append(" c => this.ValidateAndHandleAsync(c, () => this.Handle(c)),\n");
        }

        return $$"""
            // <auto-generated/>
            #nullable enable

            namespace {{grain.Namespace}}
            {
                /// <summary>Generated Orleans grain interface for {{grain.GrainName}}.</summary>
                public partial interface {{interfaceName}} : global::Edict.Core.Grains.IEdictCommandHandler
                {
                }

                public partial class {{grain.GrainName}} : {{interfaceName}}
                {
                    public override async {{EdictWellKnownNames.TaskOfEdictCommandResultFqn}} Dispatch(
                        global::Edict.Contracts.Commands.EdictCommand command)
                    {
                        global::Edict.Contracts.Results.EdictCommandResult result;
                        try
                        {
                            result = await (command switch
                            {
            {{arms.ToString().TrimEnd('\n')}}
                                _ => throw new global::Edict.Core.Sending.UnroutableCommandException(
                                    command.GetType()),
                            });
                        }
                        catch
                        {
                            this.DiscardRaisedEvents();
                            throw;
                        }
                        if (result is global::Edict.Contracts.Results.EdictCommandResult.Accepted)
                            await this.FlushRaisedEventsAsync();
                        else
                            this.DiscardRaisedEvents();
                        return result;
                    }
                }
            }

            """;
    }

    private static string EmitAlias(CommandModel command) =>
        $$"""
        // <auto-generated/>
        #nullable enable

        namespace {{command.Namespace}}
        {
            [global::Orleans.AliasAttribute("{{command.SimpleName}}")]
            public partial record {{command.SimpleName}};
        }

        """;

    private static string EmitAddEdict(ImmutableArray<GrainModel> grains)
    {
        var ordered = grains
            .OrderBy(g => g.GrainFqn, System.StringComparer.Ordinal)
            .ToArray();

        var entries = new StringBuilder();
        foreach (var grain in ordered)
        {
            var interfaceFqn =
                $"global::{grain.Namespace}.I{grain.GrainName}";

            foreach (var command in grain.Commands)
            {
                if (command.TelemeterizedProperties.IsEmpty)
                {
                    entries.Append("                [typeof(")
                        .Append(command.Fqn)
                        .Append(")] = new global::Edict.Core.Sending.CommandRoute(typeof(")
                        .Append(command.Fqn)
                        .Append("), typeof(")
                        .Append(interfaceFqn)
                        .Append("), \"")
                        .Append(grain.GrainTypeName)
                        .Append("\", command => ((")
                        .Append(command.Fqn)
                        .Append(")command).")
                        .Append(command.RouteKeyProperty)
                        .Append("),\n");
                }
                else
                {
                    var tagLines = new StringBuilder();
                    foreach (var property in command.TelemeterizedProperties)
                    {
                        var tagName = $"edict.{command.SimpleName.ToLowerInvariant()}.{property.PropertyName.ToLowerInvariant()}";
                        tagLines.Append("                        activity?.SetTag(\"")
                            .Append(tagName)
                            .Append("\", typedCommand.")
                            .Append(property.PropertyName)
                            .Append(");\n");
                    }

                    entries.Append("                [typeof(")
                        .Append(command.Fqn)
                        .Append(")] = new global::Edict.Core.Sending.CommandRoute(\n")
                        .Append("                    typeof(").Append(command.Fqn).Append("),\n")
                        .Append("                    typeof(").Append(interfaceFqn).Append("),\n")
                        .Append("                    \"").Append(grain.GrainTypeName).Append("\",\n")
                        .Append("                    command => ((").Append(command.Fqn).Append(")command).").Append(command.RouteKeyProperty).Append(",\n")
                        .Append("                    (command, activity) =>\n")
                        .Append("                    {\n")
                        .Append("                        var typedCommand = (").Append(command.Fqn).Append(")command;\n")
                        .Append(tagLines)
                        .Append("                    }),\n");
                }
            }
        }

        return $$"""
            // <auto-generated/>
            #nullable enable

            namespace Edict.Generated
            {
                public static class EdictServiceCollectionExtensions
                {
                    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddEdict(
                        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    {
                        var routes = new global::System.Collections.Generic.Dictionary<
                            global::System.Type, global::Edict.Core.Sending.CommandRoute>
                        {
            {{entries.ToString().TrimEnd('\n')}}
                        };

                        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
                            .AddSingleton<global::Edict.Core.Sending.CommandRouteResolver>(
                                services, new global::Edict.Core.Sending.CommandRouteResolver(routes));
                        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
                            .AddSingleton<global::Edict.Contracts.Sending.IEdictSender, global::Edict.Core.Sending.EdictSender>(
                                services);
                        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
                            .AddSingleton<global::System.Diagnostics.ActivitySource>(
                                services, {{EdictWellKnownNames.EdictDiagnosticsActivitySourceFqn}});

                        return services;
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
        string GrainTypeName,
        string GrainFqn,
        ImmutableArray<CommandModel> Commands);

    private sealed record CommandModel(
        string Fqn,
        string SimpleName,
        string Namespace,
        string RouteKeyProperty,
        ImmutableArray<TelemeterizedProperty> TelemeterizedProperties);

    private sealed record TelemeterizedProperty(string PropertyName);
}
