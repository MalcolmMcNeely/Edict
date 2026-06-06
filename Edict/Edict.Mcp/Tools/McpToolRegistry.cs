using System.Text.Json;

using Edict.Mcp.Configuration;
using Edict.Mcp.Docs;
using Edict.Mcp.Handlers;
using Edict.Mcp.SiloWiring;
using Edict.Mcp.Versioning;
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
            BuildConfigurationCheckReportProvider(workspaceProvider, new ConfigurationCheckScanner()),
            BuildVersionReportProvider(workspaceProvider, new EdictVersionInspector()),
            BuildSkillBodiesReportProvider(workspaceProvider, new EdictSkillsManifestInspector()),
            docs,
            workspaceProvider)
    {
    }

    internal McpToolRegistry(
        Func<CancellationToken, Task<HandlerInventory>> inventoryProvider,
        Func<CancellationToken, Task<SiloWiringReport>> siloWiringReportProvider,
        Func<CancellationToken, Task<ConfigurationCheckReport>> configurationCheckReportProvider,
        Func<CancellationToken, Task<EdictVersionReport>> versionReportProvider,
        Func<SkillBodiesReport> skillBodiesProvider,
        DocsLookup docs,
        MSBuildWorkspaceProvider workspaceProvider)
    {
        var describeMcpState = new DescribeMcpStateTool(workspaceProvider, inventoryProvider, versionReportProvider, skillBodiesProvider, () => Tools!);
        var describeGlossaryTerm = new DescribeGlossaryTermTool(docs);
        var lookupAdr = new LookupAdrTool(docs);
        var listHandlers = new ListHandlersTool(inventoryProvider, versionReportProvider);
        var listRouteKeys = new ListRouteKeysTool(inventoryProvider, versionReportProvider);
        var describeSiloWiring = new DescribeSiloWiringTool(siloWiringReportProvider, versionReportProvider);
        var checkConfiguration = new CheckConfigurationTool(configurationCheckReportProvider);
        Tools =
        [
            new McpToolDescriptor(
                Name: "edict_describe_mcp_state",
                Description: "Self-diagnostic. Reports the loaded solution path, indexed-handler count, the Edict tool-vs-library version report, and the list of MCP tools the server has registered.",
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
                Description: "Returns every consumer-defined subclass of EdictCommandHandler / EdictEventHandler / EdictSaga / EdictProjectionBuilder / EdictListProjectionBuilder in the loaded solution, each with its role, bound Command/Event types, [EdictRouteKey] property name, declaring assembly, and source location. Saga handlers also carry their effective [EdictSagaTimeout] cap: a duration literal, unbounded, or default (inherits the silo-wide EdictSagaOptions.DefaultTimeout). A Command Handler or Saga that registers a schedule (declares a HandleAsync(TMessage) returning Task<EdictScheduleResult>) also carries its schedule timeout source: inheritsSiloDefault for a Command Handler (the silo-wide EdictCommandHandlerScheduleOptions.DefaultTimeout) or inheritsSagaCap for a Saga (bounded by the saga's own cap). The per-schedule timeout: call-site argument is not statically extracted.",
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
            new McpToolDescriptor(
                Name: "edict_check_configuration",
                Description: "Reads Program.cs in the loaded solution, determines which option knobs the consumer has set inside each AddEdict* call, and returns a best-effort verdict of required-but-unset knobs: an empty Kafka BootstrapServers, an unset Postgres ConnectionString, and a soft reminder to confirm an Azure QueueServiceClient is set on the options or registered in DI. Each finding carries a severity, category, options type, knob name, message, and source location. This tool is best-effort and resolves only set-versus-not-set, not whether a value is sensible; EdictWiringValidator, which runs at host start with live DI, is ground truth.",
                InputSchema: EmptyInputSchema,
                InvokeAsync: checkConfiguration.InvokeAsync),
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

    static Func<CancellationToken, Task<ConfigurationCheckReport>> BuildConfigurationCheckReportProvider(
        MSBuildWorkspaceProvider workspaceProvider,
        ConfigurationCheckScanner scanner)
    {
        return async cancellationToken =>
        {
            var solution = await workspaceProvider.LoadSolutionAsync(cancellationToken);
            return await scanner.ScanAsync(solution, cancellationToken);
        };
    }

    static Func<CancellationToken, Task<EdictVersionReport>> BuildVersionReportProvider(
        MSBuildWorkspaceProvider workspaceProvider,
        EdictVersionInspector inspector)
    {
        var gate = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        EdictVersionReport? cachedReport = null;
        return async cancellationToken =>
        {
            if (cachedReport is not null)
            {
                return cachedReport;
            }
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (cachedReport is not null)
                {
                    return cachedReport;
                }
                var solution = await workspaceProvider.LoadSolutionAsync(cancellationToken);
                cachedReport = inspector.Inspect(solution);
                return cachedReport;
            }
            finally
            {
                gate.Release();
            }
        };
    }

    static Func<SkillBodiesReport> BuildSkillBodiesReportProvider(
        MSBuildWorkspaceProvider workspaceProvider,
        EdictSkillsManifestInspector inspector)
    {
        var gate = new object();
        SkillBodiesReport? cachedReport = null;
        return () =>
        {
            if (cachedReport is not null)
            {
                return cachedReport;
            }
            lock (gate)
            {
                cachedReport ??= inspector.Inspect(workspaceProvider.CurrentDirectory);
                return cachedReport;
            }
        };
    }

    static JsonElement ParseInputSchema(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
