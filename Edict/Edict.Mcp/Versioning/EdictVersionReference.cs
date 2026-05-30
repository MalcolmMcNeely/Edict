namespace Edict.Mcp.Versioning;

sealed record EdictVersionReference(
    string AssemblyName,
    string Version,
    IReadOnlyList<string> ProjectsReferencing);
