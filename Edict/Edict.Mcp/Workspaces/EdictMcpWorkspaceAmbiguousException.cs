namespace Edict.Mcp.Workspaces;

sealed class EdictMcpWorkspaceAmbiguousException : Exception
{
    public EdictMcpWorkspaceAmbiguousException(string directory, IReadOnlyList<string> candidates)
        : base(BuildMessage(directory, candidates))
    {
        Directory = directory;
        Candidates = candidates;
    }

    public string Directory { get; }

    public IReadOnlyList<string> Candidates { get; }

    static string BuildMessage(string directory, IReadOnlyList<string> candidates)
    {
        var joined = string.Join(", ", candidates);
        return $"Multiple solution files found in '{directory}' ({joined}). Pass --solution to disambiguate.";
    }
}
