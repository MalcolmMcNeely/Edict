using Edict.Mcp.Protocol;
using Edict.Mcp.Tools;
using Edict.Mcp.Workspaces;

using static VerifyXunit.Verifier;

namespace Edict.Mcp.Tests.Protocol;

public class McpJsonRpcRouterTests
{
    static readonly string FixtureSolutionPath = ResolveFixtureSolutionPath();

    [Fact]
    public async Task RouteAsync_ToolsList_AdvertisesEdictDescribeMcpStateAgainstFixtureSolution()
    {
        // Arrange
        var router = BuildRouterAgainstFixture();
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""";

        // Act
        var responseJson = await router.RouteAsync(request, CancellationToken.None);

        // Assert
        await Verify(responseJson);
    }

    [Fact]
    public async Task RouteAsync_ToolsCall_InvokesEdictDescribeMcpStateAgainstFixtureSolution()
    {
        // Arrange
        var router = BuildRouterAgainstFixture();
        var request = """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"edict_describe_mcp_state"}}""";

        // Act
        var responseJson = await router.RouteAsync(request, CancellationToken.None);

        // Assert
        await Verify(responseJson)
            .AddScrubber(builder =>
            {
                builder.Replace(FixtureSolutionPath.Replace("\\", "\\\\\\\\"), "{FIXTURE_SOLUTION_PATH}");
                builder.Replace(FixtureSolutionPath.Replace("\\", "\\\\"), "{FIXTURE_SOLUTION_PATH}");
                builder.Replace(FixtureSolutionPath, "{FIXTURE_SOLUTION_PATH}");
            });
    }

    [Fact]
    public async Task RouteAsync_ToolsCall_DescribeGlossaryTerm_ReturnsTermBody()
    {
        // Arrange
        var router = BuildRouterAgainstFixture();
        var request = """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"edict_describe_glossary_term","arguments":{"term":"Saga"}}}""";

        // Act
        var responseJson = await router.RouteAsync(request, CancellationToken.None);

        // Assert
        await Verify(responseJson);
    }

    [Fact]
    public async Task RouteAsync_ToolsCall_LookupAdr_ByNumber_ReturnsAdrBody()
    {
        // Arrange
        var router = BuildRouterAgainstFixture();
        var request = """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"edict_lookup_adr","arguments":{"query":"1"}}}""";

        // Act
        var responseJson = await router.RouteAsync(request, CancellationToken.None);

        // Assert
        await Verify(responseJson);
    }

    [Fact]
    public async Task RouteAsync_ToolsCall_ListHandlers_ReturnsFixtureHandlerInventory()
    {
        // Arrange
        var router = BuildRouterAgainstFixture();
        var request = """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"edict_list_handlers"}}""";

        // Act
        var responseJson = await router.RouteAsync(request, CancellationToken.None);

        // Assert
        await Verify(responseJson);
    }

    [Fact]
    public async Task RouteAsync_ToolsCall_ListRouteKeys_ReturnsFixtureRouteKeyView()
    {
        // Arrange
        var router = BuildRouterAgainstFixture();
        var request = """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"edict_list_route_keys"}}""";

        // Act
        var responseJson = await router.RouteAsync(request, CancellationToken.None);

        // Assert
        await Verify(responseJson);
    }

    [Fact]
    public async Task RouteAsync_UnknownMethod_ReturnsMethodNotFoundError()
    {
        // Arrange
        var router = BuildRouterAgainstFixture();
        var request = """{"jsonrpc":"2.0","id":3,"method":"nope/nope"}""";

        // Act
        var responseJson = await router.RouteAsync(request, CancellationToken.None);

        // Assert
        await Verify(responseJson);
    }

    static McpJsonRpcRouter BuildRouterAgainstFixture()
    {
        var workspaceProvider = new MSBuildWorkspaceProvider(
            solutionOverride: FixtureSolutionPath,
            currentDirectoryProvider: () => Path.GetTempPath());
        var registry = new McpToolRegistry(workspaceProvider);
        return new McpJsonRpcRouter(registry);
    }

    static string ResolveFixtureSolutionPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && directory.Name != "Edict.Mcp.Tests")
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate Edict.Mcp.Tests project root from base directory.");
        }
        return Path.Combine(directory.FullName, "Fixtures", "TracerBulletFixture", "TracerBulletFixture.slnx");
    }
}
