using Edict.Tests.Conformance.Commands;

namespace Edict.Azure.Tests.Commands;

[Collection(AzureClusterCollection.Name)]
public sealed class CommandValidatorTests
    : CommandValidatorScenarios<AzureClusterFixture>
{
    public CommandValidatorTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
