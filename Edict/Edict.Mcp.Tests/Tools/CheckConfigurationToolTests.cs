using System.Text.Json;

using Edict.Mcp.Configuration;
using Edict.Mcp.Handlers;
using Edict.Mcp.Tools;

using VerifyXunit;

using Xunit;

using static VerifyXunit.Verifier;

namespace Edict.Mcp.Tests.Tools;

public class CheckConfigurationToolTests
{
    [Fact]
    public Task InvokeAsync_SerialisesReportAsCamelCaseWithStringEnums()
    {
        var report = new ConfigurationCheckReport(
            ProgramSourceLocation: new SourceLocationInfo("ConsumerHost/Program.cs", Line: 1, Column: 1),
            Findings:
            [
                new ConfigurationFinding(
                    ConfigurationFindingSeverity.Error,
                    ConfigurationFindingCategory.RequiredMissing,
                    OptionsType: "EdictKafkaStreamsOptions",
                    Knob: "BootstrapServers",
                    Message: "EdictKafkaStreamsOptions.BootstrapServers is required and has no default.",
                    SourceLocation: new SourceLocationInfo("ConsumerHost/Program.cs", Line: 11, Column: 26)),
                new ConfigurationFinding(
                    ConfigurationFindingSeverity.Info,
                    ConfigurationFindingCategory.RequiredMissing,
                    OptionsType: "EdictAzureStreamsOptions",
                    Knob: "QueueServiceClient",
                    Message: "Confirm a QueueServiceClient is set somewhere.",
                    SourceLocation: new SourceLocationInfo("ConsumerHost/Program.cs", Line: 12, Column: 26)),
            ]);
        var tool = new CheckConfigurationTool(_ => Task.FromResult(report));

        return Verify(tool.InvokeAsync(arguments: null, CancellationToken.None));
    }

    [Fact]
    public async Task InvokeAsync_EmptyReport_SerialisesEmptyFindingsArray()
    {
        var report = new ConfigurationCheckReport(ProgramSourceLocation: null, Findings: []);
        var tool = new CheckConfigurationTool(_ => Task.FromResult(report));

        var json = await tool.InvokeAsync(arguments: null, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        Assert.Empty(document.RootElement.GetProperty("findings").EnumerateArray());
    }
}
