using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

using Edict.Benchmarks.Throughput.ClosedLoop;
using Edict.Benchmarks.Throughput.Cluster;
using Edict.Benchmarks.Throughput.Measurement;
using Edict.Benchmarks.Throughput.Output;
using Edict.Benchmarks.Throughput.Saturation;
using Edict.Substrate;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run -- <substrate>");
    Console.Error.WriteLine($"  substrate: all | render | {string.Join(" | ", SubstrateRegistry.All().Select(s => s.Name))}");
    Console.Error.WriteLine("  render: skip the bench, re-render throughput.md from every <substrate>-summary.json on disk");
    return 2;
}

var selector = args[0].ToLowerInvariant();
ISubstrate[] substrates;
var renderOnly = selector == "render";
if (renderOnly)
{
    substrates = [];
}
else if (selector == "all")
{
    substrates = [.. SubstrateRegistry.All()];
}
else
{
    var resolved = SubstrateRegistry.Resolve(selector);
    if (resolved is null)
    {
        Console.Error.WriteLine($"unknown substrate '{args[0]}'. Known: all, render, {string.Join(", ", SubstrateRegistry.All().Select(s => s.Name))}");
        return 2;
    }
    substrates = [resolved];
}

int[] parallelisms = [2, 16, 64];
var warmup = TimeSpan.FromSeconds(10);
var window = TimeSpan.FromSeconds(30);
var saturationParallelism = 256;
var saturationWarmup = TimeSpan.FromSeconds(20);
var saturationWindow = TimeSpan.FromSeconds(30);

var metadata = new RunMetadata(
    MachineClass: $"{RuntimeInformation.OSDescription} / {Environment.ProcessorCount} cores",
    DotnetVersion: Environment.Version.ToString(),
    GitSha: ResolveGitSha());

var runDate = DateTimeOffset.UtcNow;
var docsRoot = ResolveDocsRoot();
var rawDirectory = Path.Combine(docsRoot, "raw");
var combined = new List<ThroughputResults>();
var saturationCombined = new List<SaturationResults>();
var closedLoop = new ClosedLoopRunner();
var saturation = new SaturationRunner();
var perSubstrateRunDate = new Dictionary<string, DateTimeOffset>();

// Indirect proof that the EdictInterceptorsEnabled build property took
// effect. The Commands sweep below A/Bs the same workload against the
// base-typed call site, so a single run produces both numbers regardless
// of the toggle's value.
var interceptorsEmitted = InterceptorProbe.SendInterceptorsEmitted();
Console.WriteLine($"Send interceptors emitted: {(interceptorsEmitted ? "yes (fast path)" : "no (registrar slow path)")}");

foreach (var substrate in substrates)
{
    perSubstrateRunDate[substrate.Name] = DateTimeOffset.UtcNow;
    Console.WriteLine($"Sweeping {substrate.Name} — Command acceptance: N ∈ {{{string.Join(", ", parallelisms)}}}, warmup {warmup}, window {window}");
    var commandsResults = await closedLoop.RunCommandsSweepAsync(substrate, parallelisms, warmup, window);
    foreach (var point in commandsResults)
    {
        Console.WriteLine($"  N={point.Parallelism}: {point.CompletedCount} commands in {point.ElapsedMeasurement.TotalSeconds:F1}s — {point.EventsPerSecond:F0} EPS — {FormatHealth(point.Health)}");
    }

    // A/B: the typed scenario above exercises the interceptor fast path
    // (when emitted); the base-typed scenario below forces the registrar
    // slow path even when interceptors are present. Both must produce
    // valid runs — the second proves the perf claim at the call-site
    // level.
    Console.WriteLine($"Sweeping {substrate.Name} — Command acceptance (base-typed, slow path): N ∈ {{{string.Join(", ", parallelisms)}}}, warmup {warmup}, window {window}");
    var baseTypedResults = await closedLoop.RunCommandsBaseTypedSweepAsync(substrate, parallelisms, warmup, window);
    foreach (var point in baseTypedResults)
    {
        Console.WriteLine($"  N={point.Parallelism}: {point.CompletedCount} commands in {point.ElapsedMeasurement.TotalSeconds:F1}s — {point.EventsPerSecond:F0} EPS — {FormatHealth(point.Health)}");
    }

    Console.WriteLine($"Sweeping {substrate.Name} — Command → Event delivery: N ∈ {{{string.Join(", ", parallelisms)}}}, warmup {warmup}, window {window}");
    var eventsResults = await closedLoop.RunEventsSweepAsync(substrate, parallelisms, warmup, window);
    foreach (var point in eventsResults)
    {
        Console.WriteLine($"  N={point.Parallelism}: {point.CompletedCount} events in {point.ElapsedMeasurement.TotalSeconds:F1}s — {point.EventsPerSecond:F0} EPS — {FormatHealth(point.Health)}");
    }

    var perSubstrate = new List<ThroughputResults>(commandsResults.Count + baseTypedResults.Count + eventsResults.Count);
    perSubstrate.AddRange(commandsResults);
    perSubstrate.AddRange(baseTypedResults);
    perSubstrate.AddRange(eventsResults);
    combined.AddRange(perSubstrate);

    var closedLoopCsvPath = Path.Combine(docsRoot, "raw", $"{runDate:yyyy-MM-dd}-{substrate.Name}-closedloop.csv");
    await CsvWriter.WriteAsync(closedLoopCsvPath, perSubstrate);
    Console.WriteLine($"  Wrote {closedLoopCsvPath}");

    // Saturation pass — fresh cluster, sat-mode signal, N=256 fire-and-forget,
    // single sum-of-counters read at window-end. The closed-loop cluster was
    // torn down inside RunSweepAsync; the saturation runner brings up its own.
    Console.WriteLine($"Saturating {substrate.Name} — Events: N={saturationParallelism}, warmup {saturationWarmup}, window {saturationWindow}");
    var saturationResult = await saturation.RunAsync(
        substrate, saturationParallelism, saturationWarmup, saturationWindow);
    Console.WriteLine($"  {saturationResult.EventsPerSecond:F0} EPS (window {saturationResult.WindowSeconds}s, N={saturationResult.ProducerConcurrency}, aggregates={saturationResult.AggregateCount}) — {FormatHealth(saturationResult.Health)}");
    saturationCombined.Add(saturationResult);

    var saturationCsvPath = Path.Combine(docsRoot, "raw", $"{runDate:yyyy-MM-dd}-{substrate.Name}-saturation.csv");
    await SaturationCsvWriter.WriteAsync(saturationCsvPath, [saturationResult]);
    Console.WriteLine($"  Wrote {saturationCsvPath}");

    // Per-substrate sidecar summary — keyed by substrate name (no date in the
    // file name), so a single-substrate run only refreshes its own file. The
    // markdown render below unions every substrate's summary on disk, so
    // unrun substrates keep their published rows + run date.
    var substrateSummary = SubstrateSummaryStore.BuildFromResults(
        substrate.Name, perSubstrateRunDate[substrate.Name], perSubstrate, saturationResult);
    var substrateSummaryPath = SubstrateSummaryStore.PathFor(rawDirectory, substrate.Name);
    await SubstrateSummaryStore.WriteAsync(substrateSummaryPath, substrateSummary);
}

var templatePath = Path.Combine(docsRoot, "throughput.template.md");
var template = await File.ReadAllTextAsync(templatePath);
var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["machine_class"] = metadata.MachineClass,
    ["dotnet_version"] = metadata.DotnetVersion,
    ["git_sha"] = metadata.GitSha,
};

// Render from the union of every substrate's sidecar summary, not just the
// current run. The current run's substrates were just (over-)written above;
// any other substrate's prior summary on disk is read here, so its rows +
// run-date token survive a single-substrate invocation.
var allSummaries = await SubstrateSummaryStore.ReadAllAsync(rawDirectory);
var hydrated = SubstrateSummaryStore.Hydrate(allSummaries);
foreach (var (substrateName, substrateRunDate) in hydrated.RunDates)
{
    tokens["run_date:" + substrateName] = substrateRunDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

var markdownPath = Path.Combine(docsRoot, "throughput.md");
await MarkdownWriter.WriteAsync(markdownPath, template, tokens, hydrated.ClosedLoop, hydrated.Saturation);
Console.WriteLine($"Wrote {markdownPath}");

// Run-level health rollup: any point exceeding the failure-rate threshold
// drives a non-zero exit code so this benchmark is safe to wire into
// automation. The throughput numbers stay published either way — silently
// dropping a degraded run would defeat the whole "confidence in the
// framework" point of the bench.
var degradedClosedLoop = combined.Count(r => !r.Health.IsHealthy && r.Health.Attempted > 0);
var degradedSaturation = saturationCombined.Count(r => !r.Health.IsHealthy && r.Health.Attempted > 0);
var degradedCount = degradedClosedLoop + degradedSaturation;
if (degradedCount > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"⚠ Benchmark complete with {degradedCount} degraded point(s) above the {RunHealth.DefaultFailureRateThreshold:P0} failure-rate threshold. See {markdownPath} § Run health for the breakdown.");
    return 1;
}
return 0;

static string FormatHealth(RunHealth health)
{
    if (health.Attempted == 0)
    {
        return "no producer outcomes recorded";
    }
    var prefix = health.IsHealthy ? "OK" : "⚠ DEGRADED";
    var breakdown = health.Failed == 0
        ? string.Empty
        : $"; {health.RenderFailureTypes()}";
    return $"{prefix} ({health.Succeeded:N0} OK + {health.Failed:N0} failed, {health.FailureRate:P2}{breakdown})";
}

static string ResolveDocsRoot()
{
    // Walk up from the binary directory until we hit the docs/benchmarks tree
    // so `dotnet run` from any working directory targets the committed files.
    var directory = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(directory))
    {
        var candidate = Path.Combine(directory, "docs", "benchmarks");
        if (Directory.Exists(candidate) || Directory.Exists(Path.Combine(directory, "docs")))
        {
            return candidate;
        }
        directory = Path.GetDirectoryName(directory);
    }
    throw new InvalidOperationException("Could not locate repo's docs/ directory from " + AppContext.BaseDirectory);
}

static string ResolveGitSha()
{
    try
    {
        var info = new ProcessStartInfo("git", "rev-parse --short HEAD")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        using var process = Process.Start(info);
        if (process is null)
        {
            return "(unknown)";
        }
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(5_000);
        return process.ExitCode == 0 && output.Length > 0 ? output : "(unknown)";
    }
    catch
    {
        return "(unknown)";
    }
}
