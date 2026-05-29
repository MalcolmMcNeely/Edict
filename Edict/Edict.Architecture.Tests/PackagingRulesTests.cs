using System.Xml.Linq;

using Xunit;

namespace Edict.Architecture.Tests;

public class PackagingRulesTests
{
    static readonly string[] ShippingPackages =
    [
        "Edict.Contracts",
        "Edict.Telemetry",
        "Edict.Core",
        "Edict.Azure.Streaming",
        "Edict.Azure.Persistence",
        "Edict.Kafka",
        "Edict.Postgres",
        "Edict.Testing",
    ];

    static readonly string[] RequiredShippingProperties =
    [
        "PackageId",
        "Description",
        "PackageTags",
        "PackageReadmeFile",
    ];

    [Fact]
    public void OnlyShippingCsprojs_DeclareIsPackableTrue()
    {
        var solutionRoot = GetSolutionRoot();
        var packableProjects = EnumerateCsprojs(solutionRoot)
            .Where(csproj => ReadProperty(csproj, "IsPackable") == "true")
            .Select(csproj => Path.GetFileNameWithoutExtension(csproj))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(ShippingPackages.OrderBy(name => name, StringComparer.Ordinal), packableProjects);
    }

    [Fact]
    public void EveryNonShippingCsproj_DeclaresIsPackableFalseExplicitly()
    {
        var solutionRoot = GetSolutionRoot();
        var shipping = new HashSet<string>(ShippingPackages, StringComparer.Ordinal);

        var violations = EnumerateCsprojs(solutionRoot)
            .Where(csproj => !shipping.Contains(Path.GetFileNameWithoutExtension(csproj)))
            .Where(csproj => ReadProperty(csproj, "IsPackable") != "false")
            .Select(csproj => Path.GetFileName(csproj))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void EveryShippingCsproj_HasReadmeMarkdownSibling()
    {
        var solutionRoot = GetSolutionRoot();
        var missing = EnumerateCsprojs(solutionRoot)
            .Where(csproj => ShippingPackages.Contains(Path.GetFileNameWithoutExtension(csproj)))
            .Where(csproj => !File.Exists(Path.Combine(Path.GetDirectoryName(csproj)!, "README.md")))
            .Select(csproj => Path.GetFileNameWithoutExtension(csproj))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryShippingCsproj_DeclaresPackageMetadataProperties()
    {
        var solutionRoot = GetSolutionRoot();
        var violations = new List<string>();

        foreach (var csproj in EnumerateCsprojs(solutionRoot))
        {
            var name = Path.GetFileNameWithoutExtension(csproj);
            if (!ShippingPackages.Contains(name))
            {
                continue;
            }

            foreach (var property in RequiredShippingProperties)
            {
                if (string.IsNullOrWhiteSpace(ReadProperty(csproj, property)))
                {
                    violations.Add($"{name} missing <{property}>");
                }
            }
        }

        Assert.Empty(violations);
    }

    static IEnumerable<string> EnumerateCsprojs(string solutionRoot)
    {
        return Directory
            .EnumerateFiles(solutionRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
    }

    static string? ReadProperty(string csprojPath, string propertyName)
    {
        var document = XDocument.Load(csprojPath);
        return document
            .Descendants(propertyName)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));
    }

    static string GetSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !directory.EnumerateFiles("*.slnx").Any())
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
