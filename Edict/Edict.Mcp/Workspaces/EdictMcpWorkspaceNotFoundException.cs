namespace Edict.Mcp.Workspaces;

sealed class EdictMcpWorkspaceNotFoundException : Exception
{
    public EdictMcpWorkspaceNotFoundException(string startDirectory)
        : base($"No .slnx or .sln file found by walking up from '{startDirectory}'. Pass --solution to point the MCP server at a specific solution.")
    {
        StartDirectory = startDirectory;
    }

    public string StartDirectory { get; }
}
