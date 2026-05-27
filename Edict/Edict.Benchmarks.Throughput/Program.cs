using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

using Edict.Benchmarks.Throughput;
using Edict.Substrate;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run -- <substrate>");
    Console.Error.WriteLine($"  substrate: all | {string.Join(" | ", SubstrateRegistry.All().Select(s => s.Name))}");
    return 2;
}

var selector = args[0].ToLowerInvariant();
ISubstrate[] substrates;
if (selector == "all")
{
    substrates = [.. SubstrateRegistry.All()];
}
else
{
    var resolved = SubstrateRegistry.Resolve(selector);
    if (resolved is null)
    {
        Console.Error.WriteLine($"unknown substrate '{args[0]}'. Known: all, {string.Join(", ", SubstrateRegistry.All().Select(s => s.Name))}");
        return 2;
    }
    substrates = [resolved];
}

int[] parallelisms = [1, 4, 16, 64, 256];
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
var combined = new List<ThroughputResults>();
var saturationCombined = new List<SaturationResults>();
var runner = new ThroughputRunner();
var perSubstrateRunDate = new Dictionary<string, DateTimeOffset>();

foreach (var substrate in substrates)
{
    perSubstrateRunDate[substrate.Name] = DateTimeOffset.UtcNow;
    Console.WriteLine($"Sweeping {substrate.Name} — Commands: N ∈ {{{string.Join(", ", parallelisms)}}}, warmup {warmup}, window {window}");
    var commandsResults = await runner.RunCommandsSweepAsync(substrate, parallelisms, warmup, window);
    foreach (var point in commandsResults)
    {
        Console.WriteLine($"  N={point.Parallelism}: {point.CompletedCount} commands in {point.ElapsedMeasurement.TotalSeconds:F1}s — {point.EventsPerSecond:F0} EPS");
    }

    Console.WriteLine($"Sweeping {substrate.Name} — RaiseOnly: N ∈ {{{string.Join(", ", parallelisms)}}}, warmup {warmup}, window {window}");
    var raiseOnlyResults = await runner.RunRaiseOnlySweepAsync(substrate, parallelisms, warmup, window);
    foreach (var point in raiseOnlyResults)
    {
        Console.WriteLine($"  N={point.Parallelism}: {point.CompletedCount} sends in {point.ElapsedMeasurement.TotalSeconds:F1}s — {point.EventsPerSecond:F0} EPS");
    }

    Console.WriteLine($"Sweeping {substrate.Name} — Events: N ∈ {{{string.Join(", ", parallelisms)}}}, warmup {warmup}, window {window}");
    var eventsResults = await runner.RunEventsSweepAsync(substrate, parallelisms, warmup, window);
    foreach (var point in eventsResults)
    {
        Console.WriteLine($"  N={point.Parallelism}: {point.CompletedCount} events in {point.ElapsedMeasurement.TotalSeconds:F1}s — {point.EventsPerSecond:F0} EPS");
    }

    var perSubstrate = new List<ThroughputResults>(commandsResults.Count + raiseOnlyResults.Count + eventsResults.Count);
    perSubstrate.AddRange(commandsResults);
    perSubstrate.AddRange(raiseOnlyResults);
    perSubstrate.AddRange(eventsResults);
    combined.AddRange(perSubstrate);

    var closedLoopCsvPath = Path.Combine(docsRoot, "raw", $"{runDate:yyyy-MM-dd}-{substrate.Name}-closedloop.csv");
    await CsvWriter.WriteAsync(closedLoopCsvPath, perSubstrate);
    Console.WriteLine($"  Wrote {closedLoopCsvPath}");

    // Saturation pass — fresh cluster, sat-mode signal, N=256 fire-and-forget,
    // single sum-of-counters read at window-end. The closed-loop cluster was
    // torn down inside RunSweepAsync; the saturation runner brings up its own.
    Console.WriteLine($"Saturating {substrate.Name} — Events: N={saturationParallelism}, warmup {saturationWarmup}, window {saturationWindow}");
    var saturationResult = await runner.RunSaturationAsync(
        substrate, saturationParallelism, saturationWarmup, saturationWindow);
    Console.WriteLine($"  {saturationResult.EventsPerSecond:F0} EPS (window {saturationResult.WindowSeconds}s, N={saturationResult.ProducerConcurrency}, aggregates={saturationResult.AggregateCount})");
    saturationCombined.Add(saturationResult);

    var saturationCsvPath = Path.Combine(docsRoot, "raw", $"{runDate:yyyy-MM-dd}-{substrate.Name}-saturation.csv");
    await SaturationCsvWriter.WriteAsync(saturationCsvPath, [saturationResult]);
    Console.WriteLine($"  Wrote {saturationCsvPath}");
}

var templatePath = Path.Combine(docsRoot, "throughput.template.md");
var template = await File.ReadAllTextAsync(templatePath);
var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["machine_class"] = metadata.MachineClass,
    ["dotnet_version"] = metadata.DotnetVersion,
    ["git_sha"] = metadata.GitSha,
};
foreach (var (substrateName, substrateRunDate) in perSubstrateRunDate)
{
    tokens["run_date:" + substrateName] = substrateRunDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

var markdownPath = Path.Combine(docsRoot, "throughput.md");
await MarkdownWriter.WriteAsync(markdownPath, template, tokens, combined, saturationCombined);
Console.WriteLine($"Wrote {markdownPath}");
return 0;

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
