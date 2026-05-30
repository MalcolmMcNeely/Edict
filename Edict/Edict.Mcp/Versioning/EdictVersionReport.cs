namespace Edict.Mcp.Versioning;

sealed record EdictVersionReport(
    string ToolVersion,
    IReadOnlyList<EdictVersionReference> References,
    bool IsDrifted,
    bool HasNoEdictReferences,
    bool HasInconsistentLibraryVersions)
{
    public string DriftStatus => ClassifyDriftStatus();

    string ClassifyDriftStatus()
    {
        if (HasNoEdictReferences)
        {
            return "no-edict-references";
        }
        if (HasInconsistentLibraryVersions)
        {
            return "inconsistent-library-versions";
        }
        if (IsDrifted)
        {
            return "drifted";
        }
        return "clean";
    }
}
