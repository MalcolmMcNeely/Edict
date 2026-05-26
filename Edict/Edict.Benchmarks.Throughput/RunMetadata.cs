namespace Edict.Benchmarks.Throughput;

/// <summary>
/// Per-run context surfaced in the published markdown header so a reader can
/// judge the numbers (issue #126): host machine class, .NET runtime version,
/// and the commit the harness was built from.
/// </summary>
public sealed record RunMetadata(
    string MachineClass,
    string DotnetVersion,
    string GitSha);
