namespace Edict.Benchmarks.Throughput;

/// <summary>
/// Single place that lists every <see cref="ISubstrate"/> the harness can run.
/// Adding a future substrate is one line here — no runner / writer / dispatcher
/// changes (issue #126).
/// </summary>
public static class SubstrateRegistry
{
    static readonly ISubstrate[] Registered =
    [
        new AzuriteSubstrate(),
    ];

    public static IReadOnlyList<ISubstrate> All() => Registered;

    public static ISubstrate? Resolve(string name)
    {
        foreach (var s in Registered)
        {
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }
        }
        return null;
    }
}
