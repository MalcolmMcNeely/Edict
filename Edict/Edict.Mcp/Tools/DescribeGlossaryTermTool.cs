using System.Text.Json;

using Edict.Mcp.Docs;

namespace Edict.Mcp.Tools;

sealed class DescribeGlossaryTermTool
{
    readonly DocsLookup docs;

    public DescribeGlossaryTermTool(DocsLookup docs)
    {
        this.docs = docs;
    }

    public Task<string> InvokeAsync(IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        if (arguments is null || !arguments.TryGetValue("term", out var termElement) || termElement.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult("Missing required argument 'term' (string).");
        }

        var term = termElement.GetString();
        if (string.IsNullOrWhiteSpace(term))
        {
            return Task.FromResult("Missing required argument 'term' (string).");
        }

        var body = docs.LookupGlossaryTerm(term);
        if (body is null)
        {
            return Task.FromResult($"Glossary term '{term}' not found in CONTEXT.md.");
        }
        return Task.FromResult(body);
    }
}
