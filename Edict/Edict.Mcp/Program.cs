using System.Text.Json;

using Edict.Mcp.Tools;
using Edict.Mcp.Workspaces;

using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;

namespace Edict.Mcp;

static class Program
{
    static readonly JsonElement InputSchema = JsonSerializer.Deserialize<JsonElement>(
        """{"type":"object","properties":{}}""");

    static async Task<int> Main(string[] args)
    {
        var solutionOverride = ParseSolutionOverride(args);

        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        var workspaceProvider = new MSBuildWorkspaceProvider(
            solutionOverride: solutionOverride,
            currentDirectoryProvider: Directory.GetCurrentDirectory);
        var registry = new McpToolRegistry(workspaceProvider);

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithListToolsHandler((request, cancellationToken) =>
                ValueTask.FromResult(BuildListToolsResult(registry)))
            .WithCallToolHandler((request, cancellationToken) =>
                BuildCallToolResultAsync(registry, request.Params, cancellationToken));

        await builder.Build().RunAsync();
        return 0;
    }

    static string? ParseSolutionOverride(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == "--solution")
            {
                return args[index + 1];
            }
        }
        return null;
    }

    static ListToolsResult BuildListToolsResult(McpToolRegistry registry)
    {
        return new ListToolsResult
        {
            Tools = registry.Tools
                .Select(tool => new Tool
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = InputSchema,
                })
                .ToList(),
        };
    }

    static async ValueTask<CallToolResult> BuildCallToolResultAsync(
        McpToolRegistry registry,
        CallToolRequestParams? parameters,
        CancellationToken cancellationToken)
    {
        var toolName = parameters?.Name;
        var descriptor = toolName is null ? null : registry.Find(toolName);
        if (descriptor is null)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Unknown tool: {toolName}" }],
                IsError = true,
            };
        }

        var responseText = await descriptor.InvokeAsync(cancellationToken);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = responseText }],
        };
    }
}
