using Edict.Generators;

using Microsoft.CodeAnalysis;

namespace Edict.Mcp.Handlers;

sealed class HandlerScanner
{
    const string CommandHandlerOpenGeneric = "Edict.Core.Commands.EdictCommandHandler`1";
    const string CommandValidatorOpenGeneric = "Edict.Core.Commands.EdictCommandValidator`1";
    const string EventHandlerBase = "Edict.Core.EventHandler.EdictEventHandler";
    const string SagaOpenGeneric = "Edict.Core.Sagas.EdictSaga`1";
    const string ProjectionBuilderBase = "Edict.Core.Projections.EdictProjectionBuilder";
    const string ListProjectionBuilderOpenGeneric = "Edict.Core.Projections.EdictListProjectionBuilder`1";
    const string CommandBase = "Edict.Contracts.Commands.EdictCommand";
    const string EventBase = "Edict.Contracts.Events.EdictEvent";
    const string RouteKeyAttributeFullName = "Edict.Contracts.Commands.EdictRouteKeyAttribute";
    const string SagaTimeoutAttributeFullName = "Edict.Contracts.Sagas.EdictSagaTimeoutAttribute";
    const string ScheduleMessageBase = "Edict.Contracts.Schedules.EdictScheduleMessage";
    const string TaskOfScheduleResultReturnType = "System.Threading.Tasks.Task<Edict.Contracts.Schedules.EdictScheduleResult>";

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

            entries.Add(BuildEntry(symbol, role.Value, resolver, solutionDirectory));
        }
    }

    static HandlerEntry BuildEntry(INamedTypeSymbol handlerSymbol, HandlerRole role, BaseTypeResolver resolver, string? solutionDirectory)
    {
        var boundContracts = CollectBoundContracts(handlerSymbol, role, resolver);
        var sourceLocation = ResolveSourceLocation(handlerSymbol, solutionDirectory);
        var sagaTimeoutCap = role == HandlerRole.Saga ? ReadSagaTimeoutCap(handlerSymbol) : null;
        var schedule = ReadScheduleRegistration(handlerSymbol, role);
        return new HandlerEntry(
            DeclaringTypeName: handlerSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
            Role: role,
            BoundContracts: boundContracts,
            DeclaringAssembly: handlerSymbol.ContainingAssembly.Name,
            SourceLocation: sourceLocation,
            SagaTimeoutCap: sagaTimeoutCap,
            Schedule: schedule);
    }

    static IReadOnlyList<BoundContractInfo> CollectBoundContracts(INamedTypeSymbol handlerSymbol, HandlerRole role, BaseTypeResolver resolver)
    {
        if (role == HandlerRole.CommandValidator)
        {
            return resolver.CollectValidatorBoundCommand(handlerSymbol);
        }

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

    // Mirrors SagaTimeoutAttributeReader's runtime three-way resolution: Unbounded wins,
    // then a non-empty duration literal, otherwise the saga inherits the silo-wide default.
    // The scanner reports the source-declared cap; it cannot read EdictSagaOptions.DefaultTimeout,
    // so an un-annotated saga surfaces as Default rather than a concrete value.
    static SagaTimeoutCap ReadSagaTimeoutCap(INamedTypeSymbol sagaSymbol)
    {
        for (var current = sagaSymbol; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass is not { } attributeClass ||
                    attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)) != SagaTimeoutAttributeFullName)
                {
                    continue;
                }

                var isUnbounded = attribute.NamedArguments
                    .Any(namedArgument => namedArgument.Key == "Unbounded" && namedArgument.Value.Value is true);
                if (isUnbounded)
                {
                    return SagaTimeoutCap.Unbounded;
                }

                var duration = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;
                if (!string.IsNullOrEmpty(duration))
                {
                    return SagaTimeoutCap.ForDuration(duration);
                }

                return SagaTimeoutCap.InheritsDefault;
            }
        }
        return SagaTimeoutCap.InheritsDefault;
    }

    // A handler registers a schedule when it declares a schedule fire handler
    // HandleAsync(TMessage) : Task<EdictScheduleResult>, mirroring the generator's own discovery.
    // The scanner does not read the timeout: call-site argument: a command handler's schedule
    // inherits the silo-wide EdictCommandHandlerScheduleOptions.DefaultTimeout, while a saga's
    // schedule is bounded by the saga cap, so the source is the only thing surfaced here.
    static ScheduleRegistration? ReadScheduleRegistration(INamedTypeSymbol handlerSymbol, HandlerRole role)
    {
        if (role != HandlerRole.CommandHandler && role != HandlerRole.Saga)
        {
            return null;
        }

        foreach (var member in EnumerateInheritedMembers(handlerSymbol))
        {
            if (member is not IMethodSymbol method
                || method.Name != EdictWellKnownNames.HandleMethodName
                || method.Parameters.Length != 1)
            {
                continue;
            }
            if (method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)) != TaskOfScheduleResultReturnType)
            {
                continue;
            }
            if (method.Parameters[0].Type is not INamedTypeSymbol parameterType || !DerivesFromMetadataName(parameterType, ScheduleMessageBase))
            {
                continue;
            }
            return role == HandlerRole.Saga ? ScheduleRegistration.InheritsSagaCap : ScheduleRegistration.InheritsSiloDefault;
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
        readonly INamedTypeSymbol? commandValidatorOpen;
        readonly INamedTypeSymbol? eventHandlerBase;
        readonly INamedTypeSymbol? sagaOpen;
        readonly INamedTypeSymbol? projectionBuilderBase;
        readonly INamedTypeSymbol? listProjectionBuilderOpen;

        public BaseTypeResolver(Compilation compilation)
        {
            commandHandlerOpen = compilation.GetTypeByMetadataName(CommandHandlerOpenGeneric);
            commandValidatorOpen = compilation.GetTypeByMetadataName(CommandValidatorOpenGeneric);
            eventHandlerBase = compilation.GetTypeByMetadataName(EventHandlerBase);
            sagaOpen = compilation.GetTypeByMetadataName(SagaOpenGeneric);
            projectionBuilderBase = compilation.GetTypeByMetadataName(ProjectionBuilderBase);
            listProjectionBuilderOpen = compilation.GetTypeByMetadataName(ListProjectionBuilderOpenGeneric);
        }

        public bool HasAnyBase => commandHandlerOpen is not null
            || commandValidatorOpen is not null
            || eventHandlerBase is not null
            || sagaOpen is not null
            || projectionBuilderBase is not null
            || listProjectionBuilderOpen is not null;

        public HandlerRole? ResolveRole(INamedTypeSymbol type)
        {
            // Order matters: ListProjectionBuilder is a kind of ProjectionBuilder, so check it first.
            if (listProjectionBuilderOpen is not null && DerivesFromOpenGeneric(type, listProjectionBuilderOpen))
            {
                return HandlerRole.ListProjectionBuilder;
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
            if (commandValidatorOpen is not null && DerivesFromOpenGeneric(type, commandValidatorOpen))
            {
                return HandlerRole.CommandValidator;
            }
            if (commandHandlerOpen is not null && DerivesFromOpenGeneric(type, commandHandlerOpen))
            {
                return HandlerRole.CommandHandler;
            }
            return null;
        }

        public IReadOnlyList<BoundContractInfo> CollectValidatorBoundCommand(INamedTypeSymbol validatorSymbol)
        {
            if (commandValidatorOpen is null)
            {
                return [];
            }
            for (var current = validatorSymbol.BaseType; current is not null; current = current.BaseType)
            {
                if (!SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, commandValidatorOpen))
                {
                    continue;
                }
                if (current.TypeArguments.Length != 1 || current.TypeArguments[0] is not INamedTypeSymbol boundCommand)
                {
                    continue;
                }
                var displayName = boundCommand.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
                return [new BoundContractInfo(displayName, FindRouteKeyPropertyName(boundCommand))];
            }
            return [];
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
