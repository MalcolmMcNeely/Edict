using System.Text.Json;

using Edict.Mcp.Docs;

namespace Edict.Mcp.Tools;

sealed class LookupAdrTool
{
    readonly DocsLookup docs;

    public LookupAdrTool(DocsLookup docs)
    {
        this.docs = docs;
    }

    public Task<string> InvokeAsync(IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        if (arguments is null || !arguments.TryGetValue("query", out var queryElement) || queryElement.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult("Missing required argument 'query' (string: ADR number like '28' or '0028', or a fuzzy title substring).");
        }

        var query = queryElement.GetString();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult("Missing required argument 'query' (string: ADR number like '28' or '0028', or a fuzzy title substring).");
        }

        var body = docs.LookupAdr(query);
        if (body is null)
        {
            return Task.FromResult($"ADR matching query '{query}' not found.");
        }
        return Task.FromResult(body);
    }
}
