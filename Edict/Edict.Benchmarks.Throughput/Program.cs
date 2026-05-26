using Edict.Benchmarks.Throughput;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run -- <substrate>   (substrate: azure)");
    return 2;
}

var substrate = args[0].ToLowerInvariant() switch
{
    "azure" => (ISubstrate)new AzuriteSubstrate(),
    _ => null,
};
if (substrate is null)
{
    Console.Error.WriteLine($"unknown substrate '{args[0]}'. Known: azure");
    return 2;
}

// Tracer-bullet point: single (substrate, Commands, N=4) sample.
// The PRD sweep (N=1,4,16,64,256 × 10s warmup + 30s window) lands in a
// follow-up slice; this run is sized so `dotnet run -- azure` returns
// promptly while still exercising every load-bearing piece.
const int parallelism = 4;
var warmup = TimeSpan.FromSeconds(3);
var window = TimeSpan.FromSeconds(10);

Console.WriteLine($"Running {substrate.Name} substrate, Commands scenario, N={parallelism} (warmup {warmup}, window {window})...");

var runner = new ThroughputRunner();
var result = await runner.RunCommandsAsync(substrate, parallelism, warmup, window);

var outputPath = ResolveDocsPath();
await MarkdownWriter.WriteAsync(outputPath, [result], DateTimeOffset.UtcNow);

Console.WriteLine($"Completed {result.CompletedCount} commands in {result.ElapsedMeasurement.TotalSeconds:F1}s — {result.EventsPerSecond:F0} EPS");
Console.WriteLine($"Wrote {outputPath}");
return 0;

static string ResolveDocsPath()
{
    // Walk up from the binary directory until we hit a folder containing the
    // docs tree, so `dotnet run` from any working directory targets the
    // committed file.
    var directory = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(directory))
    {
        var candidate = Path.Combine(directory, "docs", "benchmarks");
        if (Directory.Exists(candidate) || Directory.Exists(Path.Combine(directory, "docs")))
        {
            return Path.Combine(candidate, "throughput.md");
        }
        directory = Path.GetDirectoryName(directory);
    }
    throw new InvalidOperationException("Could not locate repo's docs/ directory from " + AppContext.BaseDirectory);
}
