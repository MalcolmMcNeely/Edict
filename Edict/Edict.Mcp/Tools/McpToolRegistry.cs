using System.Text.Json;

using Edict.Mcp.Docs;
using Edict.Mcp.Handlers;
using Edict.Mcp.SiloWiring;
using Edict.Mcp.Workspaces;

namespace Edict.Mcp.Tools;

sealed class McpToolRegistry
{
    static readonly JsonElement EmptyInputSchema = ParseInputSchema(
        """{"type":"object","properties":{}}""");

    static readonly JsonElement GlossaryTermInputSchema = ParseInputSchema(
        """
        {
          "type": "object",
          "properties": {
            "term": {
              "type": "string",
              "description": "Glossary term to look up. Case-insensitive; the optional 'Edict' prefix is elidable so 'Saga', 'saga', and 'EdictSaga' all resolve to the same entry."
            }
          },
          "required": ["term"]
        }
        """);

    static readonly JsonElement LookupAdrInputSchema = ParseInputSchema(
        """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "ADR number (e.g. '28' or '0028') or a fuzzy substring of the ADR title."
            }
          },
          "required": ["query"]
        }
        """);

    public McpToolRegistry(MSBuildWorkspaceProvider workspaceProvider)
        : this(workspaceProvider, EmbeddedDocs.CreateDocsLookup(typeof(McpToolRegistry).Assembly))
    {
    }

    internal McpToolRegistry(MSBuildWorkspaceProvider workspaceProvider, DocsLookup docs)
        : this(
            BuildHandlerInventoryProvider(workspaceProvider, new HandlerScanner()),
            BuildSiloWiringReportProvider(workspaceProvider, new SiloWiringScanner()),
            docs,
            workspaceProvider)
    {
    }

    internal McpToolRegistry(
        Func<CancellationToken, Task<HandlerInventory>> inventoryProvider,
        Func<CancellationToken, Task<SiloWiringReport>> siloWiringReportProvider,
        DocsLookup docs,
        MSBuildWorkspaceProvider workspaceProvider)
    {
        var describeMcpState = new DescribeMcpStateTool(workspaceProvider, inventoryProvider, () => Tools!);
        var describeGlossaryTerm = new DescribeGlossaryTermTool(docs);
        var lookupAdr = new LookupAdrTool(docs);
        var listHandlers = new ListHandlersTool(inventoryProvider);
        var listRouteKeys = new ListRouteKeysTool(inventoryProvider);
        var describeSiloWiring = new DescribeSiloWiringTool(siloWiringReportProvider);
        Tools =
        [
            new McpToolDescriptor(
                Name: "edict_describe_mcp_state",
                Description: "Self-diagnostic. Reports the loaded solution path, indexed-handler count, and the list of MCP tools the server has registered.",
                InputSchema: EmptyInputSchema,
                InvokeAsync: describeMcpState.InvokeAsync),
            new McpToolDescriptor(
                Name: "edict_describe_glossary_term",
                Description: "Returns the Edict glossary entry for a term from CONTEXT.md, including its definition, the '_Avoid_' list, and any inline cross-references. Case-insensitive; the optional 'Edict' prefix on the query is elidable.",
                InputSchema: GlossaryTermInputSchema,
                InvokeAsync: describeGlossaryTerm.InvokeAsync),
            new McpToolDescriptor(
                Name: "edict_lookup_adr",
                Description: "Returns the raw markdown body of an Edict ADR matching the query. The query is either an ADR number ('28' or '0028') or a fuzzy substring of the ADR title.",
                InputSchema: LookupAdrInputSchema,
                InvokeAsync: lookupAdr.InvokeAsync),
            new McpToolDescriptor(
                Name: "edict_list_handlers",
                Description: "Returns every consumer-defined subclass of EdictCommandHandler / EdictEventHandler / EdictSaga / EdictProjectionBuilder / EdictTableProjectionBuilder in the loaded solution, each with its role, bound Command/Event types, [EdictRouteKey] property name, declaring assembly, and source location.",
                InputSchema: EmptyInputSchema,
                InvokeAsync: listHandlers.InvokeAsync),
            new McpToolDescriptor(
                Name: "edict_list_route_keys",
                Description: "Derived view over the handler inventory. Groups Commands by their handler classes (a Command bound to more than one handler is a collision) and Events by their subscriber classes, with the [EdictRouteKey] property name on each contract.",
                InputSchema: EmptyInputSchema,
                InvokeAsync: listRouteKeys.InvokeAsync),
            new McpToolDescriptor(
                Name: "edict_describe_silo_wiring",
                Description: "Locates Program.cs in the loaded solution, walks the ISiloBuilder invocation chain, and reports the AddEdict* extensions that are wired plus the known-but-missing ones an agent should consider before suggesting wiring changes (for example AddEdictAzureBlobClaimCheck when the consumer asks for a Claim Check setup).",
                InputSchema: EmptyInputSchema,
                InvokeAsync: describeSiloWiring.InvokeAsync),
        ];
    }

    public IReadOnlyList<McpToolDescriptor> Tools { get; }

    public McpToolDescriptor? Find(string name)
    {
        return Tools.FirstOrDefault(tool => tool.Name == name);
    }

    static Func<CancellationToken, Task<HandlerInventory>> BuildHandlerInventoryProvider(
        MSBuildWorkspaceProvider workspaceProvider,
        HandlerScanner scanner)
    {
        return async cancellationToken =>
        {
            var solution = await workspaceProvider.LoadSolutionAsync(cancellationToken);
            return await scanner.ScanAsync(solution, cancellationToken);
        };
    }

    static Func<CancellationToken, Task<SiloWiringReport>> BuildSiloWiringReportProvider(
        MSBuildWorkspaceProvider workspaceProvider,
        SiloWiringScanner scanner)
    {
        return async cancellationToken =>
        {
            var solution = await workspaceProvider.LoadSolutionAsync(cancellationToken);
            return await scanner.ScanAsync(solution, cancellationToken);
        };
    }

    static JsonElement ParseInputSchema(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
