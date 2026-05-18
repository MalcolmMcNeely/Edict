namespace Edict.Telemetry.Tests;

[CollectionDefinition(Name)]
public sealed class TelemetryClusterCollection : ICollectionFixture<TelemetryClusterFixture>
{
    public const string Name = "TelemetryCluster";
}
