using Xunit;

namespace Edict.Azure.Streaming.Tests;

/// <summary>
/// Streaming-axis saga-timeout fixture: the real Azure Queue stream provider running
/// on the base's virtual clock, so the saga absolute-lifetime-cap scenario can pump
/// the trigger over the real queue, advance past the one-second cap, and fire it
/// deterministically with no wall-clock wait. Flipping the clock on a dedicated
/// collection keeps every other AQS streaming scenario on the wall clock, where their
/// real-queue pulling agents deliver in real time.
/// </summary>
public sealed class AqsSagaTimeoutStreamingFixture : AqsStreamingFixture
{
    protected override bool UsesVirtualClock => true;
}

[CollectionDefinition(Name)]
public sealed class AqsSagaTimeoutStreamingCollection : ICollectionFixture<AqsSagaTimeoutStreamingFixture>
{
    public const string Name = "AqsSagaTimeoutStreaming";
}
