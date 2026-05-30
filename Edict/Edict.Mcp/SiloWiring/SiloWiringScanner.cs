using Edict.Mcp.Handlers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Edict.Mcp.SiloWiring;

sealed class SiloWiringScanner
{
    const string SiloBuilderFullName = "Orleans.Hosting.ISiloBuilder";
    const string ProgramFileName = "Program.cs";
    const string AddEdictPrefix = "AddEdict";

    static readonly SiloWiringEntry[] KnownExtensions =
    [
        new("AddEdict", "Edict.Core", "Registers the Edict framework: handler discovery, outbox, telemetry."),
        new("AddEdictAzureStreams", "Edict.Azure.Streaming", "Wires the Azure Queue stream provider."),
        new("AddEdictAzureBlobClaimCheck", "Edict.Azure.Streaming", "Enables the Azure Blob claim-check store for large event payloads."),
        new("AddEdictAzurePersistence", "Edict.Azure.Persistence", "Wires Azure Table Storage as the grain-state provider."),
        new("AddEdictPostgresPersistence", "Edict.Postgres", "Wires PostgreSQL as the grain-state provider."),
        new("AddEdictKafkaStreams", "Edict.Kafka", "Wires the Kafka stream provider."),
    ];

    public SiloWiringReport Scan(IEnumerable<Compilation> compilations, string? solutionDirectory)
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
        return new SiloWiringReport(ProgramSourceLocation: null, Wired: [], Missing: []);
    }

    public async Task<SiloWiringReport> ScanAsync(Solution solution, CancellationToken cancellationToken)
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

    static SiloWiringReport BuildReport(Compilation compilation, SyntaxTree programTree, string? solutionDirectory)
    {
        var semanticModel = compilation.GetSemanticModel(programTree);
        var wired = new List<SiloWiringEntry>();
        var wiredNames = new HashSet<string>(StringComparer.Ordinal);
        var invocationsInSourceOrder = programTree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .OrderBy(invocation => invocation.Expression.Span.End);
        foreach (var invocation in invocationsInSourceOrder)
        {
            var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null || !IsAddEdictOnSiloBuilder(symbol))
            {
                continue;
            }
            if (!wiredNames.Add(symbol.Name))
            {
                continue;
            }
            wired.Add(new SiloWiringEntry(
                ExtensionName: symbol.Name,
                DeclaringAssembly: symbol.ContainingAssembly.Name,
                Purpose: LookupPurpose(symbol.Name)));
        }

        var missing = KnownExtensions
            .Where(entry => !wiredNames.Contains(entry.ExtensionName))
            .ToList();

        var programLocation = ResolveProgramLocation(programTree, solutionDirectory);
        return new SiloWiringReport(programLocation, wired, missing);
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

    static string LookupPurpose(string extensionName)
    {
        var match = KnownExtensions.FirstOrDefault(entry => entry.ExtensionName == extensionName);
        return match?.Purpose ?? "Edict extension method (not in the known-extensions catalogue).";
    }

    static SourceLocationInfo? ResolveProgramLocation(SyntaxTree programTree, string? solutionDirectory)
    {
        var relativePath = RelativisePath(programTree.FilePath, solutionDirectory);
        return new SourceLocationInfo(FilePath: relativePath, Line: 1, Column: 1);
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
