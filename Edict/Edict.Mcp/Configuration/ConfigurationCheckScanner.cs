using Edict.Mcp.Handlers;
using Edict.Mcp.SiloWiring;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Mcp.Configuration;

sealed class ConfigurationCheckScanner
{
    const string SiloBuilderFullName = "Orleans.Hosting.ISiloBuilder";
    const string ProgramFileName = "Program.cs";
    const string AddEdictPrefix = "AddEdict";
    const string AuditOnSwitchName = "WithAudit";
    const string AuditResolverExtension = "AddEdictAudit";

    readonly SiloWiringScanner siloWiringScanner = new();

    public ConfigurationCheckReport Scan(IEnumerable<Compilation> compilations, string? solutionDirectory)
    {
        var compilationList = compilations as IReadOnlyCollection<Compilation> ?? compilations.ToList();
        foreach (var compilation in compilationList)
        {
            var programTree = compilation.SyntaxTrees.FirstOrDefault(IsProgramCs);
            if (programTree is null)
            {
                continue;
            }
            var wiredExtensions = siloWiringScanner.Scan(compilationList, solutionDirectory).Wired
                .Select(entry => entry.ExtensionName)
                .ToHashSet(StringComparer.Ordinal);
            return BuildReport(compilation, programTree, solutionDirectory, wiredExtensions);
        }
        return new ConfigurationCheckReport(ProgramSourceLocation: null, Findings: []);
    }

    public async Task<ConfigurationCheckReport> ScanAsync(Solution solution, CancellationToken cancellationToken)
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

    static bool IsProgramCs(SyntaxTree syntaxTree)
    {
        if (string.IsNullOrEmpty(syntaxTree.FilePath))
        {
            return false;
        }
        return string.Equals(Path.GetFileName(syntaxTree.FilePath), ProgramFileName, StringComparison.OrdinalIgnoreCase);
    }

    static ConfigurationCheckReport BuildReport(
        Compilation compilation,
        SyntaxTree programTree,
        string? solutionDirectory,
        IReadOnlySet<string> wiredExtensions)
    {
        var semanticModel = compilation.GetSemanticModel(programTree);
        var root = programTree.GetRoot();

        var wiredExtensionLocations = CollectWiredExtensions(root, semanticModel, solutionDirectory);
        var assignedKnobs = CollectAssignedKnobs(root, semanticModel, solutionDirectory);

        var findings = new List<ConfigurationFinding>();
        foreach (var entry in ConfigurationKnobCatalogue.Entries)
        {
            if (!wiredExtensionLocations.TryGetValue(entry.ConfiguringExtension, out var location))
            {
                continue;
            }
            foreach (var knob in entry.Knobs)
            {
                var isAssigned = assignedKnobs.TryGetValue((entry.OptionsTypeName, knob.Name), out var assignmentLocation);
                if (knob.Requirement != KnobRequirement.None && !isAssigned)
                {
                    findings.Add(BuildRequiredMissingFinding(entry, knob, location));
                }
                if (knob.Footgun is not null && isAssigned)
                {
                    findings.Add(BuildFootgunFinding(entry, knob, assignmentLocation ?? location));
                }
            }
        }

        var auditResolverRegistered = ProgramRegistersAuditResolver(root, semanticModel);
        findings.AddRange(BuildCrossExtensionFindings(wiredExtensions, wiredExtensionLocations, auditResolverRegistered));

        var programLocation = new SourceLocationInfo(
            FilePath: RelativisePath(programTree.FilePath, solutionDirectory),
            Line: 1,
            Column: 1);
        return new ConfigurationCheckReport(programLocation, findings);
    }

    static Dictionary<string, SourceLocationInfo?> CollectWiredExtensions(
        SyntaxNode root,
        SemanticModel semanticModel,
        string? solutionDirectory)
    {
        var wired = new Dictionary<string, SourceLocationInfo?>(StringComparer.Ordinal);
        var invocationsInSourceOrder = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .OrderBy(invocation => invocation.Expression.Span.End);
        foreach (var invocation in invocationsInSourceOrder)
        {
            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol symbol || !IsAddEdictOnSiloBuilder(symbol))
            {
                continue;
            }
            if (wired.ContainsKey(symbol.Name))
            {
                continue;
            }
            var invocationLocation = invocation.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Name.GetLocation()
                : invocation.GetLocation();
            wired[symbol.Name] = ResolveLocation(invocationLocation, solutionDirectory);
        }
        return wired;
    }

    static Dictionary<(string OptionsType, string Knob), SourceLocationInfo?> CollectAssignedKnobs(
        SyntaxNode root,
        SemanticModel semanticModel,
        string? solutionDirectory)
    {
        var assigned = new Dictionary<(string, string), SourceLocationInfo?>();
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(assignment.Left).Symbol is not IPropertySymbol property)
            {
                continue;
            }
            var containingType = property.ContainingType?.Name;
            if (containingType is null)
            {
                continue;
            }
            var key = (containingType, property.Name);
            if (assigned.ContainsKey(key))
            {
                continue;
            }
            assigned[key] = ResolveLocation(assignment.Left.GetLocation(), solutionDirectory);
        }
        return assigned;
    }

    static ConfigurationFinding BuildRequiredMissingFinding(
        ConfigurationOptionsEntry entry,
        ConfigurationKnob knob,
        SourceLocationInfo? location)
    {
        var (severity, message) = knob.Requirement switch
        {
            KnobRequirement.Required => (
                ConfigurationFindingSeverity.Error,
                $"{entry.OptionsTypeName}.{knob.Name} is required and has no default; set it in the {entry.ConfiguringExtension} configure lambda."),
            KnobRequirement.ConfirmExternally => (
                ConfigurationFindingSeverity.Info,
                $"Confirm a {knob.Name} is set somewhere: on {entry.OptionsTypeName} in the {entry.ConfiguringExtension} configure lambda, or registered in DI (for example via AddAzureClients)."),
            _ => throw new InvalidOperationException($"Knob '{knob.Name}' has no required-missing finding."),
        };
        return new ConfigurationFinding(
            severity,
            ConfigurationFindingCategory.RequiredMissing,
            entry.OptionsTypeName,
            knob.Name,
            message,
            location);
    }

    static ConfigurationFinding BuildFootgunFinding(
        ConfigurationOptionsEntry entry,
        ConfigurationKnob knob,
        SourceLocationInfo? location)
    {
        return new ConfigurationFinding(
            ConfigurationFindingSeverity.Warning,
            ConfigurationFindingCategory.Footgun,
            entry.OptionsTypeName,
            knob.Name,
            knob.Footgun!,
            location);
    }

    static IEnumerable<ConfigurationFinding> BuildCrossExtensionFindings(
        IReadOnlySet<string> wiredExtensions,
        IReadOnlyDictionary<string, SourceLocationInfo?> wiredExtensionLocations,
        bool auditResolverRegistered)
    {
        if (wiredExtensions.Contains(AuditOnSwitchName) && !auditResolverRegistered)
        {
            yield return new ConfigurationFinding(
                ConfigurationFindingSeverity.Error,
                ConfigurationFindingCategory.CrossExtension,
                OptionsType: null,
                Knob: null,
                Message: $"{AuditOnSwitchName} turns audit capture on but no principal resolver is registered: every origin send fails closed at the edge. Call services.AddEdictAudit(...) to resolve the real actor for a user-initiated send; an actor-less origin instead mints one explicitly such as EdictPrincipal.Of(\"system\") at the send.",
                wiredExtensionLocations.GetValueOrDefault(AuditOnSwitchName));
        }

        var wiredStreamProvider = ConfigurationKnobCatalogue.StreamProviderExtensions.FirstOrDefault(wiredExtensions.Contains);
        var hasPersistenceProvider = ConfigurationKnobCatalogue.PersistenceProviderExtensions.Any(wiredExtensions.Contains);
        if (wiredStreamProvider is not null && !hasPersistenceProvider)
        {
            yield return new ConfigurationFinding(
                ConfigurationFindingSeverity.Error,
                ConfigurationFindingCategory.CrossExtension,
                OptionsType: null,
                Knob: null,
                Message: $"{wiredStreamProvider} is wired but no persistence provider is: this silo cannot persist grain state. Wire AddEdictAzurePersistence or AddEdictPostgresPersistence.",
                wiredExtensionLocations.GetValueOrDefault(wiredStreamProvider));
        }

        if (wiredExtensions.Contains(ConfigurationKnobCatalogue.AzureBlobClaimCheckExtension)
            && wiredExtensions.Contains(ConfigurationKnobCatalogue.PostgresPersistenceExtension))
        {
            yield return new ConfigurationFinding(
                ConfigurationFindingSeverity.Warning,
                ConfigurationFindingCategory.CrossExtension,
                OptionsType: null,
                Knob: null,
                Message: $"{ConfigurationKnobCatalogue.AzureBlobClaimCheckExtension} is wired alongside Postgres persistence, which already provides a claim-check store: the Azure Blob claim-check store is redundant. Remove it unless you intend the claim check to live in Azure Blob storage.",
                wiredExtensionLocations.GetValueOrDefault(ConfigurationKnobCatalogue.AzureBlobClaimCheckExtension));
        }
    }

    static bool ProgramRegistersAuditResolver(SyntaxNode root, SemanticModel semanticModel)
    {
        // The resolver lands via services.AddEdictAudit(...) on the service collection, not on
        // the silo builder, so the silo-extension scan never sees it; key on the extension name.
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol symbol
                && symbol.IsExtensionMethod
                && symbol.Name == AuditResolverExtension)
            {
                return true;
            }
        }
        return false;
    }

    static bool IsAddEdictOnSiloBuilder(IMethodSymbol method)
    {
        if (!method.Name.StartsWith(AddEdictPrefix, StringComparison.Ordinal))
        {
            return false;
        }
        if (!method.IsExtensionMethod)
        {
            return false;
        }
        return method.ReceiverType is INamedTypeSymbol receiver
            && receiver.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)) == SiloBuilderFullName;
    }

    static SourceLocationInfo? ResolveLocation(Location location, string? solutionDirectory)
    {
        var lineSpan = location.GetLineSpan();
        var path = lineSpan.Path;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }
        return new SourceLocationInfo(
            FilePath: RelativisePath(path, solutionDirectory),
            Line: lineSpan.StartLinePosition.Line + 1,
            Column: lineSpan.StartLinePosition.Character + 1);
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
}
