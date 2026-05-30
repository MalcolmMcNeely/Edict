using System.Text.Json;

using Edict.Mcp.Tools;
using Edict.Mcp.Versioning;
using Edict.Mcp.Workspaces;

using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;

namespace Edict.Mcp;

static class Program
{
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

        await EmitStartupDriftCheckAsync(workspaceProvider);

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

    static async Task EmitStartupDriftCheckAsync(MSBuildWorkspaceProvider workspaceProvider)
    {
        try
        {
            var solution = await workspaceProvider.LoadSolutionAsync(CancellationToken.None);
            var report = new EdictVersionInspector().Inspect(solution);
            var message = EdictDriftStderrFormatter.Format(report);
            if (message is not null)
            {
                Console.Error.Write(message);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[edict-mcp] startup drift check skipped: {exception.Message}");
        }

        try
        {
            var skillBodiesReport = new EdictSkillsManifestInspector().Inspect(workspaceProvider.CurrentDirectory);
            var skillBodiesMessage = EdictSkillBodiesDriftStderrFormatter.Format(skillBodiesReport);
            if (skillBodiesMessage is not null)
            {
                Console.Error.WriteLine(skillBodiesMessage);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[edict-mcp] skill-body drift check skipped: {exception.Message}");
        }
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
                    InputSchema = tool.InputSchema,
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

        IReadOnlyDictionary<string, JsonElement>? arguments = parameters?.Arguments is { } argumentsDictionary
            ? new Dictionary<string, JsonElement>(argumentsDictionary)
            : null;

        var responseText = await descriptor.InvokeAsync(arguments, cancellationToken);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = responseText }],
        };
    }
}
