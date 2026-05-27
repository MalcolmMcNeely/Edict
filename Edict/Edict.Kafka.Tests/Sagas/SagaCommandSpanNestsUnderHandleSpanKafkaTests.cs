using Edict.Tests.Conformance.Sagas;

using Xunit;

namespace Edict.Kafka.Tests.Sagas;

[Collection(KafkaClusterCollection.Name)]
public sealed class SagaCommandSpanNestsUnderHandleSpanKafkaTests(KafkaClusterFixture fixture)
    : SagaCommandSpanNestsUnderHandleSpanScenarios<KafkaClusterFixture>(fixture);
