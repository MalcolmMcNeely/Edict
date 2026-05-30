using Edict.Mcp;
using Edict.Mcp.Docs;

using Xunit;

namespace Edict.Mcp.Tests.Docs;

public class DocsLookupTests
{
    [Fact]
    public void FromEmbeddedResources_LookupGlossaryTerm_ResolvesRealEdictSagaEntry()
    {
        // Arrange
        var docs = EmbeddedDocs.CreateDocsLookup(typeof(EdictMcpServer).Assembly);

        // Act
        var body = docs.LookupGlossaryTerm("EdictSaga");

        // Assert
        Assert.NotNull(body);
        Assert.Contains("Saga", body);
        Assert.Contains("_Avoid_:", body);
    }

    [Fact]
    public void FromEmbeddedResources_LookupAdr_ResolvesRealAdr0001ByNumber()
    {
        // Arrange
        var docs = EmbeddedDocs.CreateDocsLookup(typeof(EdictMcpServer).Assembly);

        // Act
        var body = docs.LookupAdr("1");

        // Assert
        Assert.NotNull(body);
        Assert.StartsWith("# Event-driven", body);
    }

    static readonly IReadOnlyList<AdrDocument> SyntheticAdrs =
    [
        new AdrDocument("0001-event-driven-not-event-sourced.md", "# Event-driven not event-sourced\n\nBody one.\n"),
        new AdrDocument("0028-custom-kafka-stream-provider.md", "# Custom Kafka stream provider\n\nBody twenty-eight.\n"),
    ];

    [Theory]
    [InlineData("28")]
    [InlineData("0028")]
    public void LookupAdr_ByNumber_ReturnsAdrBody(string query)
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: "# Edict\n", adrs: SyntheticAdrs);

        // Act
        var body = docs.LookupAdr(query);

        // Assert
        Assert.Equal("# Custom Kafka stream provider\n\nBody twenty-eight.\n", body);
    }

    [Theory]
    [InlineData("kafka")]
    [InlineData("Custom Kafka")]
    [InlineData("CUSTOM KAFKA STREAM PROVIDER")]
    public void LookupAdr_ByFuzzyTitleSubstring_ReturnsAdrBody(string query)
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: "# Edict\n", adrs: SyntheticAdrs);

        // Act
        var body = docs.LookupAdr(query);

        // Assert
        Assert.Equal("# Custom Kafka stream provider\n\nBody twenty-eight.\n", body);
    }

    const string SyntheticContextMarkdown = """
# Edict

Overview text.

## Language

**Saga**:
A grain that coordinates a multi-step workflow by reacting to Events and issuing exactly one Command per Event via `Dispatch`.
_Avoid_: dispatching more than one command per handled event.

**EdictCommand**:
An expression of intent to change state, addressed to exactly one grain.
_Avoid_: trace fields on `Command`; past-tense command names.

## Relationships

- Stuff
""";

    [Theory]
    [InlineData("Saga")]
    [InlineData("saga")]
    [InlineData("SAGA")]
    [InlineData("EdictSaga")]
    [InlineData("edictsaga")]
    public void LookupGlossaryTerm_ResolvesCaseInsensitivelyAndElidableEdictPrefix(string query)
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: SyntheticContextMarkdown, adrs: SyntheticAdrs);

        // Act
        var body = docs.LookupGlossaryTerm(query);

        // Assert
        Assert.NotNull(body);
        Assert.Contains("coordinates a multi-step workflow", body);
        Assert.Contains("_Avoid_:", body);
    }

    [Theory]
    [InlineData("EdictCommand")]
    [InlineData("Command")]
    [InlineData("command")]
    public void LookupGlossaryTerm_PrefixedEntry_AlsoResolvesWithoutPrefix(string query)
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: SyntheticContextMarkdown, adrs: SyntheticAdrs);

        // Act
        var body = docs.LookupGlossaryTerm(query);

        // Assert
        Assert.NotNull(body);
        Assert.Contains("An expression of intent to change state", body);
    }

    [Fact]
    public void LookupGlossaryTerm_UnknownTerm_ReturnsNull()
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: SyntheticContextMarkdown, adrs: SyntheticAdrs);

        // Act
        var body = docs.LookupGlossaryTerm("nonexistent");

        // Assert
        Assert.Null(body);
    }

    [Fact]
    public void LookupAdr_UnknownQuery_ReturnsNull()
    {
        // Arrange
        var docs = new DocsLookup(contextMarkdown: "# Edict\n", adrs: SyntheticAdrs);

        // Act
        var body = docs.LookupAdr("nonexistent");

        // Assert
        Assert.Null(body);
    }
}
