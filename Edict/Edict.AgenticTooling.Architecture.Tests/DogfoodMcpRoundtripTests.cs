using System.Diagnostics;
using System.Text.Json;

using Xunit;

namespace Edict.AgenticTooling.Architecture.Tests;

public class DogfoodMcpRoundtripTests
{
    static readonly string RepoRoot = ResolveRepoRoot();
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
        using var session = await McpStdioSession.LaunchAsync(McpAssemblyPath, "--solution", SolutionPath);

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

    sealed class McpStdioSession : IDisposable
    {
        static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60);

        readonly Process process;
        int nextRequestId = 1;

        McpStdioSession(Process process)
        {
            this.process = process;
        }

        public static async Task<McpStdioSession> LaunchAsync(string assemblyPath, params string[] additionalArguments)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = RepoRoot,
            };
            startInfo.ArgumentList.Add(assemblyPath);
            foreach (var argument in additionalArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start MCP process: dotnet {assemblyPath}");
            var session = new McpStdioSession(process);

            await session.InitializeAsync();
            return session;
        }

        async Task InitializeAsync()
        {
            var initializeParameters = new Dictionary<string, object?>
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new Dictionary<string, object?>(),
                ["clientInfo"] = new Dictionary<string, object?>
                {
                    ["name"] = "Edict.AgenticTooling.Architecture.Tests",
                    ["version"] = "0.0.0",
                },
            };
            await RequestAsync("initialize", initializeParameters);
            await SendNotificationAsync("notifications/initialized", parameters: null);
        }

        public async Task<JsonElement> RequestAsync(string method, IReadOnlyDictionary<string, object?>? parameters)
        {
            var requestId = nextRequestId++;
            var envelope = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = requestId,
                ["method"] = method,
            };
            if (parameters is not null)
            {
                envelope["params"] = parameters;
            }
            await WriteMessageAsync(envelope);

            using var responseCancellation = new CancellationTokenSource(ResponseTimeout);
            while (true)
            {
                var line = await ReadLineAsync(responseCancellation.Token);
                if (line is null)
                {
                    throw new InvalidOperationException(
                        $"MCP server closed stdout while waiting for response to method '{method}'. Stderr: {await DrainStandardErrorAsync()}");
                }

                var document = JsonDocument.Parse(line);
                var rootElement = document.RootElement;
                if (rootElement.TryGetProperty("id", out var idElement)
                    && idElement.ValueKind == JsonValueKind.Number
                    && idElement.GetInt32() == requestId)
                {
                    return rootElement.Clone();
                }
            }
        }

        async Task SendNotificationAsync(string method, IReadOnlyDictionary<string, object?>? parameters)
        {
            var envelope = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
            };
            if (parameters is not null)
            {
                envelope["params"] = parameters;
            }
            await WriteMessageAsync(envelope);
        }

        async Task WriteMessageAsync(IReadOnlyDictionary<string, object?> envelope)
        {
            var serialized = JsonSerializer.Serialize(envelope);
            await process.StandardInput.WriteLineAsync(serialized);
            await process.StandardInput.FlushAsync();
        }

        async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            return await process.StandardOutput.ReadLineAsync(cancellationToken);
        }

        async Task<string> DrainStandardErrorAsync()
        {
            try
            {
                return await process.StandardError.ReadToEndAsync();
            }
            catch
            {
                return "<stderr unavailable>";
            }
        }

        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.Close();
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
            }
            process.Dispose();
        }
    }
}
