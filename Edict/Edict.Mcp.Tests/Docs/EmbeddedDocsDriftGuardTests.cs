using Edict.Mcp;
using Edict.Mcp.Docs;

using Xunit;

namespace Edict.Mcp.Tests.Docs;

public class EmbeddedDocsDriftGuardTests
{
    static readonly string RepoRoot = ResolveRepoRoot();

    [Fact]
    public void ContextMarkdown_IsEmbeddedInMcpAssembly()
    {
        // Arrange
        var assembly = typeof(EdictMcpServer).Assembly;

        // Act
        using var stream = assembly.GetManifestResourceStream(EmbeddedDocs.ContextResourceName);

        // Assert
        Assert.NotNull(stream);
    }

    [Fact]
    public void EmbeddedAdrFileNames_MatchDocsAdrFolderContents()
    {
        // Arrange
        var assembly = typeof(EdictMcpServer).Assembly;
        var embeddedFileNames = EmbeddedDocs
            .EnumerateAdrResources(assembly)
            .Select(resource => resource.FileName)
            .OrderBy(fileName => fileName, StringComparer.Ordinal)
            .ToArray();
        var onDiskFileNames = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "docs", "adr"), "*.md")
            .Select(Path.GetFileName)
            .OrderBy(fileName => fileName, StringComparer.Ordinal)
            .ToArray();

        // Act + Assert
        Assert.Equal(onDiskFileNames, embeddedFileNames);
    }

    static string ResolveRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTEXT.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs", "adr")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate Edict repo root (looking for CONTEXT.md + docs/adr/).");
    }
}
