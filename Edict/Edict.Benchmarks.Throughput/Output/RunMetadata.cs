namespace Edict.Benchmarks.Throughput.Output;

/// <summary>
/// Per-run context surfaced in the published markdown header so a reader can
/// judge the numbers: host machine class, .NET runtime version, and the commit
/// the harness was built from.
/// </summary>
public sealed record RunMetadata(
    string MachineClass,
    string DotnetVersion,
    string GitSha);
