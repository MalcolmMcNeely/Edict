using System.Xml.Linq;

using Xunit;

namespace Edict.Architecture.Tests;

public class TestPackHoistTests
{
    static readonly string[] NonTestProjectsAllowedToReferenceXunit =
    [
        "Edict.Tests.Conformance",
    ];

    [Fact]
    public void XunitTestPack_IsDeclaredIffProjectNameEndsWithTests()
    {
        var repositoryRoot = GetRepositoryRoot();
        var hoistAppliesToProjectName = LoadHoistedXunitPredicate(repositoryRoot);
        var allowList = new HashSet<string>(NonTestProjectsAllowedToReferenceXunit, StringComparer.Ordinal);

        var violations = new List<string>();
        foreach (var csproj in EnumerateCsprojs(repositoryRoot))
        {
            var projectName = Path.GetFileNameWithoutExtension(csproj);
            if (allowList.Contains(projectName))
            {
                continue;
            }

            var isTestProject = projectName.EndsWith(".Tests", StringComparison.Ordinal);
            var hasInlineXunit = HasInlineXunitReference(csproj);
            var effectiveXunit = hasInlineXunit || hoistAppliesToProjectName(projectName);

            if (isTestProject != effectiveXunit)
            {
                violations.Add($"{projectName}: isTestProject={isTestProject}, effectiveXunit={effectiveXunit} (inline={hasInlineXunit})");
            }
        }

        Assert.Empty(violations);
    }

    static Func<string, bool> LoadHoistedXunitPredicate(string solutionRoot)
    {
        var propsPath = Path.Combine(solutionRoot, "Directory.Build.props");
        if (!File.Exists(propsPath))
        {
            return _ => false;
        }

        var document = XDocument.Load(propsPath);
        var hoistedXunitItemGroup = document
            .Descendants("ItemGroup")
            .FirstOrDefault(itemGroup =>
                itemGroup.Elements("PackageReference")
                    .Any(packageReference => (string?)packageReference.Attribute("Include") == "xunit"));

        if (hoistedXunitItemGroup is null)
        {
            return _ => false;
        }

        var condition = ((string?)hoistedXunitItemGroup.Attribute("Condition") ?? string.Empty).Trim();
        if (condition == "$(MSBuildProjectName.EndsWith('.Tests'))")
        {
            return projectName => projectName.EndsWith(".Tests", StringComparison.Ordinal);
        }

        return _ => true;
    }

    static bool HasInlineXunitReference(string csprojPath)
    {
        var document = XDocument.Load(csprojPath);
        return document
            .Descendants("PackageReference")
            .Any(packageReference => (string?)packageReference.Attribute("Include") == "xunit");
    }

    static IEnumerable<string> EnumerateCsprojs(string solutionRoot)
    {
        return Directory
            .EnumerateFiles(solutionRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
    }

    static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !directory.EnumerateFiles("Directory.Build.props").Any())
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
