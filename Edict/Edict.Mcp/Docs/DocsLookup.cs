namespace Edict.Mcp.Docs;

sealed class DocsLookup
{
    readonly string contextMarkdown;
    readonly IReadOnlyList<AdrDocument> adrs;

    public DocsLookup(string contextMarkdown, IReadOnlyList<AdrDocument> adrs)
    {
        this.contextMarkdown = contextMarkdown;
        this.adrs = adrs;
    }

    public string? LookupAdr(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        if (int.TryParse(query, out var number))
        {
            var prefix = number.ToString("D4") + "-";
            var matchedByNumber = adrs.FirstOrDefault(adr => adr.FileName.StartsWith(prefix, StringComparison.Ordinal));
            if (matchedByNumber is not null)
            {
                return matchedByNumber.Markdown;
            }
        }

        var matchedByTitle = adrs.FirstOrDefault(adr =>
            ExtractH1Title(adr.Markdown).Contains(query, StringComparison.OrdinalIgnoreCase));
        return matchedByTitle?.Markdown;
    }

    public string? LookupGlossaryTerm(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var normalisedQuery = NormaliseTermName(query);
        foreach (var entry in EnumerateGlossaryEntries(contextMarkdown))
        {
            if (NormaliseTermName(entry.TermName) == normalisedQuery)
            {
                return entry.Body;
            }
        }
        return null;
    }

    static string NormaliseTermName(string termName)
    {
        var lower = termName.ToLowerInvariant();
        var compacted = new string(lower.Where(character => char.IsLetterOrDigit(character)).ToArray());
        return compacted.StartsWith("edict", StringComparison.Ordinal) ? compacted[5..] : compacted;
    }

    static IEnumerable<GlossaryEntry> EnumerateGlossaryEntries(string markdown)
    {
        var lines = markdown.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (!line.StartsWith("**", StringComparison.Ordinal))
            {
                continue;
            }

            var closingBold = line.IndexOf("**", startIndex: 2, StringComparison.Ordinal);
            if (closingBold < 0)
            {
                continue;
            }
            var termName = line.Substring(2, closingBold - 2);

            var bodyStart = index;
            var bodyEnd = lines.Length;
            for (var lookahead = index + 1; lookahead < lines.Length; lookahead++)
            {
                var candidate = lines[lookahead].TrimEnd('\r');
                if (candidate.StartsWith("**", StringComparison.Ordinal) || candidate.StartsWith("## ", StringComparison.Ordinal))
                {
                    bodyEnd = lookahead;
                    break;
                }
            }

            var body = string.Join('\n', lines[bodyStart..bodyEnd]).TrimEnd('\n', '\r');
            yield return new GlossaryEntry(termName, body);
        }
    }

    sealed record GlossaryEntry(string TermName, string Body);

    static string ExtractH1Title(string markdown)
    {
        var firstLine = markdown.AsSpan();
        var newline = firstLine.IndexOf('\n');
        var headingSpan = newline >= 0 ? firstLine[..newline] : firstLine;
        var heading = headingSpan.ToString().TrimEnd('\r').TrimStart();
        return heading.StartsWith("# ", StringComparison.Ordinal) ? heading[2..] : string.Empty;
    }
}
