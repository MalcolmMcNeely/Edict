namespace Edict.Core.Tests.TestSupport;

// Serialises the cluster-free unit classes that either register a process-wide
// ActivityListener on the "Edict" source or assert the absence of a recording
// span. Run in parallel, one class's AllData listener makes another's
// span-absence assertion see a recording publish span, so they must not overlap.
[CollectionDefinition(Name)]
public sealed class EdictListenerUnitCollection
{
    public const string Name = "EdictListenerUnit";
}
