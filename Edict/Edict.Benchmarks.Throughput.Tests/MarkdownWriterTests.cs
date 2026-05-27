using Edict.Benchmarks.Throughput;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class MarkdownWriterTests
{
    [Fact]
    public void Render_ShouldSubstituteSingleToken()
    {
        var output = MarkdownWriter.Render(
            template: "hello {{name}}",
            tokens: new Dictionary<string, string> { ["name"] = "world" },
            results: []);

        Assert.Equal("hello world", output);
    }

    [Fact]
    public void Render_ShouldSubstituteMultipleDistinctTokens()
    {
        var output = MarkdownWriter.Render(
            template: "{{greeting}}, {{name}}!",
            tokens: new Dictionary<string, string>
            {
                ["greeting"] = "hello",
                ["name"] = "world",
            },
            results: []);

        Assert.Equal("hello, world!", output);
    }

    [Fact]
    public void Render_ShouldSubstituteRepeatedToken()
    {
        var output = MarkdownWriter.Render(
            template: "{{x}} and {{x}} again",
            tokens: new Dictionary<string, string> { ["x"] = "Y" },
            results: []);

        Assert.Equal("Y and Y again", output);
    }

    [Fact]
    public void Render_ShouldSubstituteTokenInsideMarkdownTableCell()
    {
        var output = MarkdownWriter.Render(
            template: "| col | val |\n| --- | --- |\n| sha | {{git_sha}} |\n",
            tokens: new Dictionary<string, string> { ["git_sha"] = "abc1234" },
            results: []);

        Assert.Equal(
            "| col | val |\n| --- | --- |\n| sha | abc1234 |\n",
            output);
    }

    [Fact]
    public void Render_ShouldPassMarkdownSpecialCharactersInValuesThrough()
    {
        var output = MarkdownWriter.Render(
            template: "value={{v}}",
            tokens: new Dictionary<string, string> { ["v"] = "a|b*c" },
            results: []);

        Assert.Equal("value=a|b*c", output);
    }

    [Fact]
    public void Render_ShouldNotModifyTemplate_WhenDictionaryHasExtraToken()
    {
        var output = MarkdownWriter.Render(
            template: "plain text, no placeholders",
            tokens: new Dictionary<string, string> { ["unused"] = "anything" },
            results: []);

        Assert.Equal("plain text, no placeholders", output);
    }

    [Fact]
    public void Render_ShouldLeaveLiteralTokenAndWriteWarning_WhenDictionaryMissesTemplateToken()
    {
        var warnings = new StringWriter();
        var output = MarkdownWriter.Render(
            template: "version={{dotnet_version}}",
            tokens: new Dictionary<string, string>(),
            results: [],
            warningSink: warnings);

        Assert.Equal("version={{dotnet_version}}", output);
        Assert.Contains("dotnet_version", warnings.ToString());
    }

    [Fact]
    public void Render_ShouldEmitClosedLoopTableWithHeaderAndRow_ForPopulatedResults()
    {
        var results = new[]
        {
            new ThroughputResults(
                Substrate: "azure",
                Scenario: "Commands",
                Parallelism: 4,
                CompletedCount: 6_560,
                ElapsedMeasurement: TimeSpan.FromSeconds(10),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(5.77),
                    P95: TimeSpan.FromMilliseconds(9.09),
                    P99: TimeSpan.FromMilliseconds(11.29))),
        };

        var output = MarkdownWriter.Render(
            template: "{{table:closed_loop}}",
            tokens: new Dictionary<string, string>(),
            results: results);

        var expected =
            "| Substrate | Scenario | Parallelism | Events per second (EPS) | p50 (ms) | p95 (ms) | p99 (ms) |\n" +
            "| --- | --- | --- | ---: | ---: | ---: | ---: |\n" +
            "| azure | Commands | 4 | 656 | 5.77 | 9.09 | 11.29 |";

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Render_ShouldEmitClosedLoopTableHeaderOnly_ForEmptyResults()
    {
        var output = MarkdownWriter.Render(
            template: "{{table:closed_loop}}",
            tokens: new Dictionary<string, string>(),
            results: []);

        var expected =
            "| Substrate | Scenario | Parallelism | Events per second (EPS) | p50 (ms) | p95 (ms) | p99 (ms) |\n" +
            "| --- | --- | --- | ---: | ---: | ---: | ---: |";

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Render_ShouldEmitSaturationTableWithHeaderAndRow_ForPopulatedSaturationResults()
    {
        var saturation = new[]
        {
            new SaturationResults(
                Substrate: "azure",
                EventsPerSecond: 73.4,
                WindowSeconds: 30,
                ProducerConcurrency: 256,
                AggregateCount: 1024),
            new SaturationResults(
                Substrate: "kafkapostgres",
                EventsPerSecond: 412.6,
                WindowSeconds: 30,
                ProducerConcurrency: 256,
                AggregateCount: 1024),
        };

        var output = MarkdownWriter.Render(
            template: "{{table:saturation}}",
            tokens: new Dictionary<string, string>(),
            results: [],
            saturation: saturation);

        var expected =
            "| Substrate | Events per second |\n" +
            "| --- | ---: |\n" +
            "| azure | 73 |\n" +
            "| kafkapostgres | 413 |";

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Render_ShouldEmitSaturationTableHeaderOnly_ForEmptySaturationResults()
    {
        var output = MarkdownWriter.Render(
            template: "{{table:saturation}}",
            tokens: new Dictionary<string, string>(),
            results: [],
            saturation: []);

        var expected =
            "| Substrate | Events per second |\n" +
            "| --- | ---: |";

        Assert.Equal(expected, output);
    }
}
