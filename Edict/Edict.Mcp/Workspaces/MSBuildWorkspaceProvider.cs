using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Edict.Mcp.Workspaces;

sealed class MSBuildWorkspaceProvider
{
    readonly string? solutionOverride;
    readonly Func<string> currentDirectoryProvider;
    readonly SemaphoreSlim loadGate = new(initialCount: 1, maxCount: 1);
    Solution? loadedSolution;

    public MSBuildWorkspaceProvider(string? solutionOverride, Func<string> currentDirectoryProvider)
    {
        this.solutionOverride = solutionOverride;
        this.currentDirectoryProvider = currentDirectoryProvider;
    }

    public string ResolveSolutionPath()
    {
        if (solutionOverride is not null)
        {
            return Path.GetFullPath(solutionOverride);
        }

        var startDirectory = currentDirectoryProvider();
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var candidates = directory
                .EnumerateFiles("*.slnx")
                .Concat(directory.EnumerateFiles("*.sln"))
                .Select(file => file.FullName)
                .ToList();
            if (candidates.Count == 1)
            {
                return candidates[0];
            }
            if (candidates.Count > 1)
            {
                throw new EdictMcpWorkspaceAmbiguousException(directory.FullName, candidates);
            }
            directory = directory.Parent;
        }

        throw new EdictMcpWorkspaceNotFoundException(startDirectory);
    }

    public async Task<Solution> LoadSolutionAsync(CancellationToken cancellationToken)
    {
        if (loadedSolution is not null)
        {
            return loadedSolution;
        }

        await loadGate.WaitAsync(cancellationToken);
        try
        {
            if (loadedSolution is not null)
            {
                return loadedSolution;
            }

            var solutionPath = ResolveSolutionPath();
            var workspace = MSBuildWorkspace.Create();
            loadedSolution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
            return loadedSolution;
        }
        finally
        {
            loadGate.Release();
        }
    }
}
