using System.Text.Json;

using Edict.Mcp.Docs;
using Edict.Mcp.Tools;

using Xunit;

namespace Edict.Mcp.Tests.Tools;

public class LookupAdrToolTests
{
    static readonly IReadOnlyList<AdrDocument> SyntheticAdrs =
    [
        new AdrDocument("0028-custom-kafka-stream-provider.md", "# Custom Kafka stream provider\n\nBody text.\n"),
    ];

    [Fact]
    public async Task InvokeAsync_ByNumber_ReturnsAdrMarkdownBody()
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: string.Empty, adrs: SyntheticAdrs);
        var tool = new LookupAdrTool(docs);
        var arguments = ParseArguments("""{"query":"28"}""");

        // Act
        var responseText = await tool.InvokeAsync(arguments, CancellationToken.None);

        // Assert
        Assert.Equal("# Custom Kafka stream provider\n\nBody text.\n", responseText);
    }

    [Fact]
    public async Task InvokeAsync_ByFuzzyTitle_ReturnsAdrMarkdownBody()
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: string.Empty, adrs: SyntheticAdrs);
        var tool = new LookupAdrTool(docs);
        var arguments = ParseArguments("""{"query":"kafka stream"}""");

        // Act
        var responseText = await tool.InvokeAsync(arguments, CancellationToken.None);

        // Assert
        Assert.Equal("# Custom Kafka stream provider\n\nBody text.\n", responseText);
    }

    [Fact]
    public async Task InvokeAsync_UnknownQuery_ReturnsExplanatoryNotFoundMessage()
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: string.Empty, adrs: SyntheticAdrs);
        var tool = new LookupAdrTool(docs);
        var arguments = ParseArguments("""{"query":"9999"}""");

        // Act
        var responseText = await tool.InvokeAsync(arguments, CancellationToken.None);

        // Assert
        Assert.Contains("9999", responseText);
        Assert.Contains("not found", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_MissingQueryArgument_ReturnsExplanatoryError()
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: string.Empty, adrs: SyntheticAdrs);
        var tool = new LookupAdrTool(docs);

        // Act
        var responseText = await tool.InvokeAsync(arguments: null, CancellationToken.None);

        // Assert
        Assert.Contains("query", responseText, StringComparison.OrdinalIgnoreCase);
    }

    static IReadOnlyDictionary<string, JsonElement>? ParseArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
