using Edict.Azure.Tests.Outbox;
using Edict.Tests.Conformance.Telemetry;

using Xunit;

namespace Edict.Azure.Tests.Telemetry;

[Collection(AzureOutboxControllableExecutorCollection.Name)]
public sealed class MetricsEmitOnExpectedEventsAzurePendingCountTests(AzureOutboxDeadLetterClusterFixture fixture)
    : OutboxPendingCountMetricsScenarios<AzureOutboxDeadLetterClusterFixture>(fixture);
