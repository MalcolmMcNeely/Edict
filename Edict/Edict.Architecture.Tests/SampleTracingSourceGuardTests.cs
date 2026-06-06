using Xunit;

namespace Edict.Architecture.Tests;

// The whole Edict telemetry promise is that an operator follows causality from
// one Edict span without the underlying SDK noise drowning the dashboard. A live
// "many orphaned HttpRequestIn traces" complaint was investigated and proven
// stale: the committed Edict-only wiring exports zero AspNetCore spans. These
// assertions are the regression guard against that observation recurring — they
// lock that every Sample host registers ONLY the Edict ActivitySource and that
// ServiceDefaults adds no instrumentation of its own. A future change that drops
// AddAspNetCoreInstrumentation (or any foreign AddSource) back in fails here.
public class SampleTracingSourceGuardTests
{
    static readonly string[] HostPrograms =
    [
        "Sample.Azure.Silo",
        "Sample.Azure.Web",
        "Sample.KafkaPostgres.Silo",
        "Sample.KafkaPostgres.Web",
    ];

    // Instrumentation registrations that would re-introduce the SDK-span noise the
    // Edict-only wiring deliberately drops.
    static readonly string[] ForeignInstrumentation =
    [
        "AddAspNetCoreInstrumentation",
        "AddHttpClientInstrumentation",
        "AddGrpcClientInstrumentation",
        "AddSqlClientInstrumentation",
        "AddEntityFrameworkCoreInstrumentation",
    ];

    [Theory]
    [InlineData("Sample.Azure.Silo")]
    [InlineData("Sample.Azure.Web")]
    [InlineData("Sample.KafkaPostgres.Silo")]
    [InlineData("Sample.KafkaPostgres.Web")]
    public void SampleHost_ShouldRegisterOnlyTheEdictActivitySource(string projectName)
    {
        var program = ReadProgram(projectName);

        Assert.Contains("AddSource(EdictDiagnostics.SourceName)", program);

        // A string-literal AddSource is a foreign source by definition — the Edict
        // source is always registered via the EdictDiagnostics.SourceName constant.
        Assert.DoesNotContain("AddSource(\"", program);

        foreach (var instrumentation in ForeignInstrumentation)
        {
            Assert.DoesNotContain(instrumentation, program);
        }
    }

    [Fact]
    public void ServiceDefaults_ShouldNotRegisterAnyTracingSourceOrInstrumentation()
    {
        // ServiceDefaults owns the shared OpenTelemetry plumbing; source
        // registration is delegated to each host so the shared layer must add
        // neither a source nor any instrumentation.
        var path = Path.Combine(SolutionRoot, "Sample", "Sample.ServiceDefaults", "Extensions.cs");
        Assert.True(File.Exists(path), "Sample.ServiceDefaults\\Extensions.cs missing");
        var extensions = StripLineComments(File.ReadAllText(path));

        Assert.DoesNotContain("AddSource(", extensions);

        foreach (var instrumentation in ForeignInstrumentation)
        {
            Assert.DoesNotContain(instrumentation, extensions);
        }
    }

    static string ReadProgram(string projectName)
    {
        var path = Path.Combine(SolutionRoot, "Sample", projectName, "Program.cs");
        Assert.True(File.Exists(path), $"{projectName}\\Program.cs missing");
        return StripLineComments(File.ReadAllText(path));
    }

    // Cuts each line at its first // so the assertions scan real wiring, not the
    // explanatory comments that legitimately name the same APIs.
    static string StripLineComments(string source) =>
        string.Join(
            '\n',
            source.Split('\n').Select(line =>
            {
                var commentStart = line.IndexOf("//", StringComparison.Ordinal);
                return commentStart >= 0 ? line[..commentStart] : line;
            }));

    static string SolutionRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            {
                dir = dir.Parent;
            }
            return dir?.Parent?.FullName ?? AppContext.BaseDirectory;
        }
    }
}
