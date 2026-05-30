using Edict.Mcp.Versioning;

namespace Edict.Mcp.Tests.Versioning;

static class StubVersionReportProvider
{
    public static Func<CancellationToken, Task<EdictVersionReport>> Clean(string toolVersion = "0.1.0-preview.42")
    {
        var report = new EdictVersionReport(
            ToolVersion: toolVersion,
            References: Array.Empty<EdictVersionReference>(),
            IsDrifted: false,
            HasNoEdictReferences: true,
            HasInconsistentLibraryVersions: false);
        return _ => Task.FromResult(report);
    }

    public static Func<CancellationToken, Task<EdictVersionReport>> ForReport(EdictVersionReport report)
    {
        return _ => Task.FromResult(report);
    }
}
