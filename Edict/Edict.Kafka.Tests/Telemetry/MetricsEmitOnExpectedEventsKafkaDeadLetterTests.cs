using Edict.Kafka.Tests.DeadLetter;
using Edict.Tests.Conformance.Telemetry;

using Xunit;

namespace Edict.Kafka.Tests.Telemetry;

[Collection(KafkaOutboxControllableExecutorCollection.Name)]
public sealed class MetricsEmitOnExpectedEventsKafkaDeadLetterTests(KafkaOutboxControllableExecutorFixture fixture)
    : DeadLetterPromotionMetricsScenarios<KafkaOutboxControllableExecutorFixture>(fixture);
