using System.Text.Json;

using Edict.Mcp.Docs;
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
    {
        var describeMcpState = new DescribeMcpStateTool(workspaceProvider, () => Tools!);
        var describeGlossaryTerm = new DescribeGlossaryTermTool(docs);
        var lookupAdr = new LookupAdrTool(docs);
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
        ];
    }

    public IReadOnlyList<McpToolDescriptor> Tools { get; }

    public McpToolDescriptor? Find(string name)
    {
        return Tools.FirstOrDefault(tool => tool.Name == name);
    }

    static JsonElement ParseInputSchema(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
