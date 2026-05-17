namespace Edict.Core.Tests;

// The cluster-backed tests share one TestCluster and run sequentially. A
// per-class cluster ran them in parallel, and the process-global
// ActivityListener in CommandTelemetryTests collects spans into an
// unsynchronised List<Activity> — concurrent activity-stop callbacks from
// other parallel clusters dropped entries and the telemetry test lost its
// own span. One shared, serialised collection removes the race and the
// redundant parallel clusters.
[CollectionDefinition(Name)]
public sealed class EdictClusterCollection : ICollectionFixture<EdictClusterFixture>
{
    public const string Name = "EdictCluster";
}
