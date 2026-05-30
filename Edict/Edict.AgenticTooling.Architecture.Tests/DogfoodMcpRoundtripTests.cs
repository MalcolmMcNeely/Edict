using Xunit;

namespace Edict.AgenticTooling.Architecture.Tests;

public class DogfoodMcpRoundtripTests
{
    static readonly string RepoRoot = AgenticToolingTestPaths.RepoRoot;
    static readonly string McpAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Edict.Mcp.dll");
    static readonly string SolutionPath = Path.Combine(RepoRoot, "Edict", "Edict.slnx");

    static readonly string[] ExpectedTools =
    {
        "edict_describe_glossary_term",
        "edict_lookup_adr",
        "edict_list_handlers",
        "edict_list_route_keys",
        "edict_describe_silo_wiring",
        "edict_describe_mcp_state",
    };

    [Fact]
    public async Task LiveMcpServer_AdvertisesEdictToolsAndReportsThisRepoSolutionPath()
    {
        using var session = await McpStdioSession.LaunchAsync(McpAssemblyPath, RepoRoot, "--solution", SolutionPath);

        var listToolsResponse = await session.RequestAsync("tools/list", parameters: null);
        var advertisedTools = listToolsResponse
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();

        foreach (var expectedTool in ExpectedTools)
        {
            Assert.Contains(expectedTool, advertisedTools);
        }

        var describeStateParameters = new Dictionary<string, object?>
        {
            ["name"] = "edict_describe_mcp_state",
        };
        var describeStateResponse = await session.RequestAsync("tools/call", describeStateParameters);
        var responseText = describeStateResponse
            .GetProperty("result")
            .GetProperty("content")
            .EnumerateArray()
            .First()
            .GetProperty("text")
            .GetString();

        Assert.NotNull(responseText);
        Assert.Contains("Edict.slnx", responseText, StringComparison.Ordinal);
    }
}
