using System.Text.Json;

using Edict.Mcp.Docs;
using Edict.Mcp.Tools;

using Xunit;

namespace Edict.Mcp.Tests.Tools;

public class DescribeGlossaryTermToolTests
{
    const string SyntheticContext = """
# Edict

## Language

**Saga**:
A grain that coordinates a multi-step workflow.
_Avoid_: dispatching more than one command per handled event.
""";

    [Fact]
    public async Task InvokeAsync_KnownTerm_ReturnsMarkdownBody()
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: SyntheticContext, adrs: []);
        var tool = new DescribeGlossaryTermTool(docs);
        var arguments = ParseArguments("""{"term":"saga"}""");

        // Act
        var responseText = await tool.InvokeAsync(arguments, CancellationToken.None);

        // Assert
        Assert.Contains("**Saga**:", responseText);
        Assert.Contains("coordinates a multi-step workflow", responseText);
        Assert.Contains("_Avoid_:", responseText);
    }

    [Fact]
    public async Task InvokeAsync_UnknownTerm_ReturnsExplanatoryNotFoundMessage()
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: SyntheticContext, adrs: []);
        var tool = new DescribeGlossaryTermTool(docs);
        var arguments = ParseArguments("""{"term":"nonexistent"}""");

        // Act
        var responseText = await tool.InvokeAsync(arguments, CancellationToken.None);

        // Assert
        Assert.Contains("nonexistent", responseText);
        Assert.Contains("not found", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_MissingTermArgument_ReturnsExplanatoryError()
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: SyntheticContext, adrs: []);
        var tool = new DescribeGlossaryTermTool(docs);

        // Act
        var responseText = await tool.InvokeAsync(arguments: null, CancellationToken.None);

        // Assert
        Assert.Contains("term", responseText, StringComparison.OrdinalIgnoreCase);
    }

    static IReadOnlyDictionary<string, JsonElement>? ParseArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
