namespace Edict.Architecture.Tests;

// Pure binding-completeness matcher for the axis-conformance batteries: given the
// scenario set a battery defines and the scenarios each provider actually binds,
// it reports every gap. There is no opt-out list — full within-battery symmetry is
// the invariant, so a provider that skips a scenario, or binds one from another
// battery, is a gap. Matching is exact (set difference), so a scenario name that is
// a substring of another never counts as binding it.
static class ConformanceBindingCompleteness
{
    public static IReadOnlyCollection<string> FindBindingGaps(
        IReadOnlySet<string> batteryScenarios,
        IReadOnlyDictionary<string, IReadOnlySet<string>> providerScenarioBindings)
    {
        var gaps = new List<string>();
        foreach (var (provider, bound) in providerScenarioBindings.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            foreach (var scenario in batteryScenarios.Except(bound).OrderBy(name => name, StringComparer.Ordinal))
            {
                gaps.Add($"{provider} does not bind battery scenario {scenario}");
            }
            foreach (var scenario in bound.Except(batteryScenarios).OrderBy(name => name, StringComparer.Ordinal))
            {
                gaps.Add($"{provider} binds {scenario}, which is not in this battery");
            }
        }
        return gaps;
    }
}
