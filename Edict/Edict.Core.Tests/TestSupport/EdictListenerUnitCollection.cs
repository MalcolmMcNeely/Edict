namespace Edict.Core.Tests.TestSupport;

// Serialises the cluster-free unit classes that either register a process-wide
// ActivityListener on the "Edict" source or assert the absence of a recording
// span. Run in parallel, one class's AllData listener makes another's
// span-absence assertion see a recording publish span, so they must not overlap.
// A shared collection only serialises classes within it; ActivityListeners are
// process-global, so a listener registered by any other collection running in
// parallel would still leak in. DisableParallelization moves this collection to
// xUnit's sequential phase, which runs alone with no parallel collection active,
// so the span-absence assertions see a genuinely listener-free process.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EdictListenerUnitCollection
{
    public const string Name = "EdictListenerUnit";
}
