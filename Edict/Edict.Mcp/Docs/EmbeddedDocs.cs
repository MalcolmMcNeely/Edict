using System.Reflection;

namespace Edict.Mcp.Docs;

static class EmbeddedDocs
{
    public const string ResourcePrefix = "Edict.Mcp.Docs.";
    public const string AdrResourcePrefix = "Edict.Mcp.Docs.Adr.";
    public const string ContextResourceName = "Edict.Mcp.Docs.CONTEXT.md";

    public static DocsLookup CreateDocsLookup(Assembly assembly)
    {
        var contextMarkdown = ReadResource(assembly, ContextResourceName);
        var adrs = EnumerateAdrResources(assembly)
            .Select(resource => new AdrDocument(resource.FileName, resource.Markdown))
            .ToArray();
        return new DocsLookup(contextMarkdown, adrs);
    }

    public static IEnumerable<(string FileName, string Markdown)> EnumerateAdrResources(Assembly assembly)
    {
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(AdrResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }
            var fileName = resourceName[AdrResourcePrefix.Length..];
            var markdown = ReadResource(assembly, resourceName);
            yield return (fileName, markdown);
        }
    }

    static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is not present in assembly '{assembly.FullName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
