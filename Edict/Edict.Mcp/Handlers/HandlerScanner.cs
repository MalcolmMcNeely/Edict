using Edict.Generators;

using Microsoft.CodeAnalysis;

namespace Edict.Mcp.Handlers;

sealed class HandlerScanner
{
    const string CommandHandlerOpenGeneric = "Edict.Core.Commands.EdictCommandHandler`1";
    const string EventHandlerBase = "Edict.Core.EventHandler.EdictEventHandler";
    const string SagaOpenGeneric = "Edict.Core.Sagas.EdictSaga`1";
    const string ProjectionBuilderBase = "Edict.Core.Projections.EdictProjectionBuilder";
    const string TableProjectionBuilderOpenGeneric = "Edict.Core.Projections.EdictTableProjectionBuilder`1";
    const string CommandBase = "Edict.Contracts.Commands.EdictCommand";
    const string EventBase = "Edict.Contracts.Events.EdictEvent";
    const string RouteKeyAttributeFullName = "Edict.Contracts.Commands.EdictRouteKeyAttribute";

    public HandlerInventory Scan(IEnumerable<Compilation> compilations, string? solutionDirectory)
    {
        var entries = new List<HandlerEntry>();
        foreach (var compilation in compilations)
        {
            ScanCompilation(compilation, solutionDirectory, entries);
        }
        return new HandlerInventory(entries);
    }

    public async Task<HandlerInventory> ScanAsync(Solution solution, CancellationToken cancellationToken)
    {
        var compilations = new List<Compilation>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is not null)
            {
                compilations.Add(compilation);
            }
        }
        var solutionDirectory = solution.FilePath is null ? null : Path.GetDirectoryName(solution.FilePath);
        return Scan(compilations, solutionDirectory);
    }

    static void ScanCompilation(Compilation compilation, string? solutionDirectory, List<HandlerEntry> entries)
    {
        var resolver = new BaseTypeResolver(compilation);
        if (!resolver.HasAnyBase)
        {
            return;
        }

        foreach (var symbol in EnumerateAllNamedTypes(compilation.Assembly.GlobalNamespace))
        {
            if (symbol.IsAbstract || symbol.TypeKind != TypeKind.Class || symbol.DeclaredAccessibility == Accessibility.Private)
            {
                continue;
            }

            var role = resolver.ResolveRole(symbol);
            if (role is null)
            {
                continue;
            }

            entries.Add(BuildEntry(symbol, role.Value, solutionDirectory));
        }
    }

    static HandlerEntry BuildEntry(INamedTypeSymbol handlerSymbol, HandlerRole role, string? solutionDirectory)
    {
        var boundContracts = CollectBoundContracts(handlerSymbol, role);
        var sourceLocation = ResolveSourceLocation(handlerSymbol, solutionDirectory);
        return new HandlerEntry(
            DeclaringTypeName: handlerSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
            Role: role,
            BoundContracts: boundContracts,
            DeclaringAssembly: handlerSymbol.ContainingAssembly.Name,
            SourceLocation: sourceLocation);
    }

    static IReadOnlyList<BoundContractInfo> CollectBoundContracts(INamedTypeSymbol handlerSymbol, HandlerRole role)
    {
        var expectedContractBase = role == HandlerRole.CommandHandler ? CommandBase : EventBase;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var bound = new List<BoundContractInfo>();
        foreach (var member in EnumerateInheritedMembers(handlerSymbol))
        {
            if (member is not IMethodSymbol method || method.Name != EdictWellKnownNames.HandleMethodName || method.Parameters.Length != 1)
            {
                continue;
            }
            var parameterType = method.Parameters[0].Type;
            if (parameterType is not INamedTypeSymbol named || !DerivesFromMetadataName(named, expectedContractBase))
            {
                continue;
            }
            var displayName = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
            if (!seen.Add(displayName))
            {
                continue;
            }
            bound.Add(new BoundContractInfo(displayName, FindRouteKeyPropertyName(named)));
        }
        return bound;
    }

    static IEnumerable<ISymbol> EnumerateInheritedMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                yield return member;
            }
        }
    }

    static string? FindRouteKeyPropertyName(INamedTypeSymbol contractType)
    {
        for (var current = contractType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol property)
                {
                    continue;
                }
                foreach (var attribute in property.GetAttributes())
                {
                    if (attribute.AttributeClass is { } attributeClass &&
                        attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)) == RouteKeyAttributeFullName)
                    {
                        return property.Name;
                    }
                }
            }
        }
        return null;
    }

    static SourceLocationInfo? ResolveSourceLocation(INamedTypeSymbol symbol, string? solutionDirectory)
    {
        // Sort declaring references so partial classes resolve to the same file
        // on every run regardless of Roslyn's enumeration order.
        var orderedReferences = symbol.DeclaringSyntaxReferences
            .Where(reference => !string.IsNullOrEmpty(reference.SyntaxTree.FilePath))
            .OrderBy(reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal);
        foreach (var reference in orderedReferences)
        {
            var span = reference.SyntaxTree.GetLineSpan(reference.Span);
            var relativePath = RelativisePath(span.Path, solutionDirectory);
            return new SourceLocationInfo(
                FilePath: relativePath,
                Line: span.StartLinePosition.Line + 1,
                Column: span.StartLinePosition.Character + 1);
        }
        return null;
    }

    static string RelativisePath(string absoluteOrDocumentPath, string? solutionDirectory)
    {
        if (string.IsNullOrEmpty(solutionDirectory))
        {
            return absoluteOrDocumentPath;
        }
        var normalisedRoot = solutionDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (absoluteOrDocumentPath.StartsWith(normalisedRoot, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = absoluteOrDocumentPath[normalisedRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return remainder.Replace('\\', '/');
        }
        return absoluteOrDocumentPath;
    }

    static IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(INamespaceOrTypeSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol namespaceSymbol:
                    foreach (var nested in EnumerateAllNamedTypes(namespaceSymbol))
                    {
                        yield return nested;
                    }
                    break;
                case INamedTypeSymbol namedType:
                    yield return namedType;
                    foreach (var nested in EnumerateAllNamedTypes(namedType))
                    {
                        yield return nested;
                    }
                    break;
            }
        }
    }

    static bool DerivesFromMetadataName(INamedTypeSymbol type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)) == metadataName)
            {
                return true;
            }
        }
        return false;
    }

    sealed class BaseTypeResolver
    {
        readonly INamedTypeSymbol? commandHandlerOpen;
        readonly INamedTypeSymbol? eventHandlerBase;
        readonly INamedTypeSymbol? sagaOpen;
        readonly INamedTypeSymbol? projectionBuilderBase;
        readonly INamedTypeSymbol? tableProjectionBuilderOpen;

        public BaseTypeResolver(Compilation compilation)
        {
            commandHandlerOpen = compilation.GetTypeByMetadataName(CommandHandlerOpenGeneric);
            eventHandlerBase = compilation.GetTypeByMetadataName(EventHandlerBase);
            sagaOpen = compilation.GetTypeByMetadataName(SagaOpenGeneric);
            projectionBuilderBase = compilation.GetTypeByMetadataName(ProjectionBuilderBase);
            tableProjectionBuilderOpen = compilation.GetTypeByMetadataName(TableProjectionBuilderOpenGeneric);
        }

        public bool HasAnyBase => commandHandlerOpen is not null
            || eventHandlerBase is not null
            || sagaOpen is not null
            || projectionBuilderBase is not null
            || tableProjectionBuilderOpen is not null;

        public HandlerRole? ResolveRole(INamedTypeSymbol type)
        {
            // Order matters: TableProjectionBuilder is a kind of ProjectionBuilder, so check it first.
            if (tableProjectionBuilderOpen is not null && DerivesFromOpenGeneric(type, tableProjectionBuilderOpen))
            {
                return HandlerRole.TableProjectionBuilder;
            }
            if (projectionBuilderBase is not null && DerivesFromSymbol(type, projectionBuilderBase))
            {
                return HandlerRole.ProjectionBuilder;
            }
            if (sagaOpen is not null && DerivesFromOpenGeneric(type, sagaOpen))
            {
                return HandlerRole.Saga;
            }
            if (eventHandlerBase is not null && DerivesFromSymbol(type, eventHandlerBase))
            {
                return HandlerRole.EventHandler;
            }
            if (commandHandlerOpen is not null && DerivesFromOpenGeneric(type, commandHandlerOpen))
            {
                return HandlerRole.CommandHandler;
            }
            return null;
        }

        static bool DerivesFromSymbol(INamedTypeSymbol type, INamedTypeSymbol target)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, target))
                {
                    return true;
                }
            }
            return false;
        }

        static bool DerivesFromOpenGeneric(INamedTypeSymbol type, INamedTypeSymbol openGeneric)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, openGeneric))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
