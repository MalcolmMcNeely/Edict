namespace Edict.Telemetry.Tests;

// Serialises the pure-unit classes that attach a process-wide ActivityListener
// to the single "Edict" source. Without this they run in parallel and each
// listener captures the other class's spans, so an Assert.Single over the
// collected activities sees a foreign span and fails.
[CollectionDefinition(Name)]
public sealed class ActivityListenerUnitCollection
{
    public const string Name = "ActivityListenerUnit";
}
