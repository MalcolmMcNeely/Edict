using Edict.Tests.Conformance.Telemetry;

using Xunit;

namespace Edict.Azure.Tests.Telemetry;

[Collection(AzureClusterCollection.Name)]
public sealed class EventTelemeterizedTagsOnSpansTests(AzureClusterFixture fixture)
    : EventTelemeterizedTagsOnSpansScenarios<AzureClusterFixture>(fixture);
