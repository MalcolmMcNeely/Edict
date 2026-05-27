using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Kafka.Tests.Outbox;

[Collection(KafkaClusterCollection.Name)]
public sealed class OutboxHappyPathKafkaTests
    : OutboxHappyPathScenarios<KafkaClusterFixture>
{
    public OutboxHappyPathKafkaTests(KafkaClusterFixture fixture)
        : base(fixture)
    {
    }
}
