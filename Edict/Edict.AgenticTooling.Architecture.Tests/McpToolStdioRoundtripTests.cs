using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using VerifyTests;
using VerifyXunit;

using Xunit;

using static VerifyXunit.Verifier;

namespace Edict.AgenticTooling.Architecture.Tests;

[CollectionDefinition(nameof(FixtureLibraryStdioCollection))]
public sealed class FixtureLibraryStdioCollection : ICollectionFixture<FixtureLibraryStdioServer> { }

[CollectionDefinition(nameof(FixtureLibraryWithSubmitOrderStdioCollection))]
public sealed class FixtureLibraryWithSubmitOrderStdioCollection : ICollectionFixture<FixtureLibraryWithSubmitOrderStdioServer> { }

[Collection(nameof(FixtureLibraryStdioCollection))]
public class FixtureLibraryStdioRoundtripTests
{
    readonly FixtureLibraryStdioServer server;

    public FixtureLibraryStdioRoundtripTests(FixtureLibraryStdioServer server)
    {
        this.server = server;
    }

    [Fact]
    public Task ListHandlers_ReturnsBeforeFixtureInventory() =>
        VerifyToolCall(server.Session, "edict_list_handlers");

    [Fact]
    public Task ListRouteKeys_ReturnsBeforeFixtureRouteKeyView() =>
        VerifyToolCall(server.Session, "edict_list_route_keys");

    [Fact]
    public Task DescribeSiloWiring_ReturnsBeforeFixtureWiringReport() =>
        VerifyToolCall(server.Session, "edict_describe_silo_wiring");

    [Fact]
    public Task DescribeGlossaryTerm_DomainStream_ReturnsTermBody() =>
        VerifyToolCall(server.Session, "edict_describe_glossary_term",
            new Dictionary<string, object?> { ["term"] = "Domain Stream" });

    [Fact]
    public Task LookupAdr_ByNumber_ReturnsAdrBody() =>
        VerifyToolCall(server.Session, "edict_lookup_adr",
            new Dictionary<string, object?> { ["query"] = "1" });

    static async Task VerifyToolCall(McpStdioSession session, string toolName, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var responseText = await McpToolStdioInvoker.InvokeAsync(session, toolName, arguments);
        await Verify(responseText).AddScrubber(McpToolStdioScrubbers.Apply);
    }
}

[Collection(nameof(FixtureLibraryWithSubmitOrderStdioCollection))]
public class FixtureLibraryWithSubmitOrderStdioRoundtripTests
{
    readonly FixtureLibraryWithSubmitOrderStdioServer server;

    public FixtureLibraryWithSubmitOrderStdioRoundtripTests(FixtureLibraryWithSubmitOrderStdioServer server)
    {
        this.server = server;
    }

    [Fact]
    public Task ListHandlers_ReturnsAfterFixtureInventory() =>
        VerifyToolCall(server.Session, "edict_list_handlers");

    [Fact]
    public Task ListRouteKeys_ReturnsAfterFixtureRouteKeyView() =>
        VerifyToolCall(server.Session, "edict_list_route_keys");

    [Fact]
    public Task DescribeSiloWiring_ReturnsAfterFixtureWiringReport() =>
        VerifyToolCall(server.Session, "edict_describe_silo_wiring");

    static async Task VerifyToolCall(McpStdioSession session, string toolName, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var responseText = await McpToolStdioInvoker.InvokeAsync(session, toolName, arguments);
        await Verify(responseText).AddScrubber(McpToolStdioScrubbers.Apply);
    }
}

static class McpToolStdioInvoker
{
    public static async Task<string> InvokeAsync(McpStdioSession session, string toolName, IReadOnlyDictionary<string, object?>? arguments)
    {
        var parameters = new Dictionary<string, object?> { ["name"] = toolName };
        if (arguments is not null)
        {
            parameters["arguments"] = arguments;
        }
        var response = await session.RequestAsync("tools/call", parameters);
        return response
            .GetProperty("result")
            .GetProperty("content")
            .EnumerateArray()
            .First()
            .GetProperty("text")
            .GetString()
            ?? throw new InvalidOperationException($"MCP tool '{toolName}' returned no text payload.");
    }
}

static class McpToolStdioScrubbers
{
    static readonly Regex ToolVersionPattern = new(
        "\"toolVersion\"\\s*:\\s*\"[^\"]*\"",
        RegexOptions.Compiled);

    static readonly Regex EdictVersionPattern = new(
        "\"version\"\\s*:\\s*\"[^\"]*\"",
        RegexOptions.Compiled);

    static readonly Regex FilePathPattern = new(
        "\"filePath\"\\s*:\\s*\"(?<value>[^\"]*)\"",
        RegexOptions.Compiled);

    public static void Apply(StringBuilder builder)
    {
        var scrubbed = builder.ToString();
        scrubbed = ScrubRepoRoot(scrubbed);
        scrubbed = NormaliseFilePathSeparators(scrubbed);
        scrubbed = ToolVersionPattern.Replace(scrubbed, "\"toolVersion\": \"{TOOL_VERSION}\"");
        scrubbed = EdictVersionPattern.Replace(scrubbed, "\"version\": \"{EDICT_VERSION}\"");
        builder.Clear();
        builder.Append(scrubbed);
    }

    static string ScrubRepoRoot(string text)
    {
        var repoRoot = AgenticToolingTestPaths.RepoRoot;
        var jsonEscapedRepoRoot = repoRoot.Replace("\\", "\\\\");
        var forwardSlashRepoRoot = repoRoot.Replace("\\", "/");
        return text
            .Replace(jsonEscapedRepoRoot, "{REPO_ROOT}")
            .Replace(forwardSlashRepoRoot, "{REPO_ROOT}")
            .Replace(repoRoot, "{REPO_ROOT}");
    }

    static string NormaliseFilePathSeparators(string text) =>
        FilePathPattern.Replace(text, match =>
        {
            var normalised = match.Groups["value"].Value.Replace("\\\\", "/").Replace("\\", "/");
            return $"\"filePath\": \"{normalised}\"";
        });
}

public sealed class FixtureLibraryStdioServer : McpStdioServer
{
    public FixtureLibraryStdioServer() : base("TracerBulletFixture.slnx") { }
}

public sealed class FixtureLibraryWithSubmitOrderStdioServer : McpStdioServer
{
    public FixtureLibraryWithSubmitOrderStdioServer() : base("TracerBulletFixtureWithSubmitOrder.slnx") { }
}

public abstract class McpStdioServer : IAsyncLifetime
{
    static readonly string McpAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Edict.Mcp.dll");
    static readonly string FixturesRoot = Path.Combine(
        AgenticToolingTestPaths.RepoRoot, "Edict", "Edict.Mcp.Tests", "Fixtures", "TracerBulletFixture");

    readonly string solutionFileName;
    McpStdioSession? session;

    protected McpStdioServer(string solutionFileName)
    {
        this.solutionFileName = solutionFileName;
    }

    public McpStdioSession Session =>
        session ?? throw new InvalidOperationException("MCP stdio server has not been initialized.");

    public async Task InitializeAsync()
    {
        var solutionPath = Path.Combine(FixturesRoot, solutionFileName);
        session = await McpStdioSession.LaunchAsync(McpAssemblyPath, AgenticToolingTestPaths.RepoRoot, "--solution", solutionPath);
    }

    public Task DisposeAsync()
    {
        session?.Dispose();
        return Task.CompletedTask;
    }
}
