using Edict.Tests.Conformance.Outbox;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// Azurite binding for <see cref="OutboxHappyPathScenarios{TFixture}"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class OutboxHappyPathTests : OutboxHappyPathScenarios<AzureClusterFixture>
{
    public OutboxHappyPathTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
