namespace Edict.Mcp.Tests;

sealed class TempWorkspaceDirectory : IDisposable
{
    public string Path { get; }

    public TempWorkspaceDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "edict-mcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string WriteFile(string relativePath, string contents = "")
    {
        var absolute = System.IO.Path.Combine(Path, relativePath);
        var parent = System.IO.Path.GetDirectoryName(absolute);
        if (parent is not null && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }
        File.WriteAllText(absolute, contents);
        return absolute;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
