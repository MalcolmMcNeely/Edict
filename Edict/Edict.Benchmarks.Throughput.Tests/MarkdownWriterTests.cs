using Edict.Benchmarks.Throughput.ClosedLoop;
using Edict.Benchmarks.Throughput.Measurement;
using Edict.Benchmarks.Throughput.Output;
using Edict.Benchmarks.Throughput.Saturation;

using static VerifyXunit.Verifier;

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
    public Task Render_ShouldEmitClosedLoopTable()
    {
        // Mix of rows that must be kept (Command acceptance / Command → Event delivery
        // at N ∈ {2,16,64}) and rows that must be filtered out (RaiseOnly is dropped
        // entirely; the surviving scenarios at N ∉ {2,16,64} are also dropped). The
        // closed-loop section's curated invariant is enforced by the writer.
        var results = new[]
        {
            new ThroughputResults(
                Substrate: "azure",
                Scenario: "Command acceptance",
                Parallelism: 1,
                CompletedCount: 100,
                ElapsedMeasurement: TimeSpan.FromSeconds(10),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(1),
                    P95: TimeSpan.FromMilliseconds(2),
                    P99: TimeSpan.FromMilliseconds(3))),
            new ThroughputResults(
                Substrate: "azure",
                Scenario: "Command acceptance",
                Parallelism: 2,
                CompletedCount: 200,
                ElapsedMeasurement: TimeSpan.FromSeconds(10),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(4.10),
                    P95: TimeSpan.FromMilliseconds(5.20),
                    P99: TimeSpan.FromMilliseconds(6.30))),
            new ThroughputResults(
                Substrate: "azure",
                Scenario: "RaiseOnly",
                Parallelism: 16,
                CompletedCount: 300,
                ElapsedMeasurement: TimeSpan.FromSeconds(10),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(7),
                    P95: TimeSpan.FromMilliseconds(8),
                    P99: TimeSpan.FromMilliseconds(9))),
            new ThroughputResults(
                Substrate: "azure",
                Scenario: "Command → Event delivery",
                Parallelism: 16,
                CompletedCount: 400,
                ElapsedMeasurement: TimeSpan.FromSeconds(10),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(10.40),
                    P95: TimeSpan.FromMilliseconds(15.55),
                    P99: TimeSpan.FromMilliseconds(20.66))),
            new ThroughputResults(
                Substrate: "azure",
                Scenario: "Command → Event delivery",
                Parallelism: 64,
                CompletedCount: 500,
                ElapsedMeasurement: TimeSpan.FromSeconds(10),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(12.34),
                    P95: TimeSpan.FromMilliseconds(18.99),
                    P99: TimeSpan.FromMilliseconds(25.01))),
            new ThroughputResults(
                Substrate: "azure",
                Scenario: "Command → Event delivery",
                Parallelism: 256,
                CompletedCount: 600,
                ElapsedMeasurement: TimeSpan.FromSeconds(10),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(99),
                    P95: TimeSpan.FromMilliseconds(99),
                    P99: TimeSpan.FromMilliseconds(99))),
        };

        var output = MarkdownWriter.Render(
            template: "{{table:closed_loop}}",
            tokens: new Dictionary<string, string>(),
            results: results);

        return Verify(output);
    }

    [Fact]
    public Task Render_ShouldEmitEmptyClosedLoopTable()
    {
        var output = MarkdownWriter.Render(
            template: "{{table:closed_loop}}",
            tokens: new Dictionary<string, string>(),
            results: []);

        return Verify(output);
    }

    [Fact]
    public Task Render_ShouldEmitSaturationTable()
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

        return Verify(output);
    }

    [Fact]
    public Task Render_ShouldEmitEmptySaturationTable()
    {
        var output = MarkdownWriter.Render(
            template: "{{table:saturation}}",
            tokens: new Dictionary<string, string>(),
            results: [],
            saturation: []);

        return Verify(output);
    }
}
