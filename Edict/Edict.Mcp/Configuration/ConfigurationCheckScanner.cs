using Edict.Mcp.Handlers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Mcp.Configuration;

sealed class ConfigurationCheckScanner
{
    const string SiloBuilderFullName = "Orleans.Hosting.ISiloBuilder";
    const string ProgramFileName = "Program.cs";
    const string AddEdictPrefix = "AddEdict";

    public ConfigurationCheckReport Scan(IEnumerable<Compilation> compilations, string? solutionDirectory)
    {
        foreach (var compilation in compilations)
        {
            var programTree = compilation.SyntaxTrees.FirstOrDefault(IsProgramCs);
            if (programTree is null)
            {
                continue;
            }
            return BuildReport(compilation, programTree, solutionDirectory);
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

    static ConfigurationCheckReport BuildReport(Compilation compilation, SyntaxTree programTree, string? solutionDirectory)
    {
        var semanticModel = compilation.GetSemanticModel(programTree);
        var root = programTree.GetRoot();

        var wiredExtensionLocations = CollectWiredExtensions(root, semanticModel, solutionDirectory);
        var assignedKnobs = CollectAssignedKnobs(root, semanticModel);

        var findings = new List<ConfigurationFinding>();
        foreach (var entry in ConfigurationKnobCatalogue.Entries)
        {
            if (!wiredExtensionLocations.TryGetValue(entry.ConfiguringExtension, out var location))
            {
                continue;
            }
            foreach (var knob in entry.Knobs)
            {
                if (knob.Requirement == KnobRequirement.None)
                {
                    continue;
                }
                if (assignedKnobs.Contains((entry.OptionsTypeName, knob.Name)))
                {
                    continue;
                }
                findings.Add(BuildRequiredMissingFinding(entry, knob, location));
            }
        }

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

    static HashSet<(string OptionsType, string Knob)> CollectAssignedKnobs(SyntaxNode root, SemanticModel semanticModel)
    {
        var assigned = new HashSet<(string, string)>();
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
            assigned.Add((containingType, property.Name));
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
