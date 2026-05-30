namespace Edict.AgenticTooling.Architecture.Tests;

static class AgenticToolingTestPaths
{
    public static string RepoRoot { get; } = ResolveRepoRoot();

    static string ResolveRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "Edict"))
                && Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root from base directory.");
    }
}
