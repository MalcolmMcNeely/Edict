using Xunit;

namespace Edict.Kafka.Tests;

/// <summary>
/// Streaming-axis saga-timeout fixture: the real Edict Kafka stream provider running
/// on the base's virtual clock, so the saga absolute-lifetime-cap scenario can pump
/// the trigger over the real broker, advance past the one-second cap, and fire it
/// deterministically with no wall-clock wait. Flipping the clock on a dedicated
/// collection keeps every other Kafka streaming scenario on the wall clock, where
/// their real-broker pulling agents deliver in real time.
/// </summary>
public sealed class KafkaSagaTimeoutStreamingFixture : KafkaStreamingFixture
{
    protected override bool UsesVirtualClock => true;
}

[CollectionDefinition(Name)]
public sealed class KafkaSagaTimeoutStreamingCollection : ICollectionFixture<KafkaSagaTimeoutStreamingFixture>
{
    public const string Name = "KafkaSagaTimeoutStreaming";
}
