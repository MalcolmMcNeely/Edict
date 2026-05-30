using Microsoft.CodeAnalysis;

namespace Edict.Mcp.Tests.Versioning;

static class SyntheticSolution
{
    public static Solution WithProjects(params (string ProjectName, IReadOnlyList<string> ReferenceDllPaths)[] projects)
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        foreach (var (projectName, referenceDllPaths) in projects)
        {
            var projectId = ProjectId.CreateNewId();
            var metadataReferences = referenceDllPaths
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToList();
            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Default,
                projectName,
                projectName,
                LanguageNames.CSharp,
                metadataReferences: metadataReferences);
            solution = solution.AddProject(projectInfo);
        }
        return solution;
    }
}
