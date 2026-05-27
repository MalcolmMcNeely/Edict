using Edict.Tests.Conformance.Events;

using Xunit;

namespace Edict.Kafka.Tests.Events;

/// <summary>
/// Tracer bullet for #142: bind the substrate-neutral
/// <see cref="AcceptedCommandPublishesEventScenarios{TFixture}"/> battery to
/// the Kafka-streams × Postgres-persistence fixture. Proves the seam compiles
/// and an event round-trips through real Kafka end-to-end. The rest of the
/// happy-path conformance subset follows in slice 2 (#142 acceptance).
/// </summary>
[Collection(KafkaClusterCollection.Name)]
public sealed class AcceptedCommandPublishesEventKafkaTests
    : AcceptedCommandPublishesEventScenarios<KafkaClusterFixture>
{
    public AcceptedCommandPublishesEventKafkaTests(KafkaClusterFixture fixture)
        : base(fixture)
    {
    }
}
