using Edict.Tests.Conformance.Commands;

namespace Edict.Azure.Tests.Commands;

/// <summary>
/// Azurite/Testcontainers binding for
/// <see cref="CommandPipelineEndToEndScenarios{TFixture}"/>. Inherits the
/// scenario battery from <c>Edict.Tests.Conformance</c>; the [Fact] methods
/// run unmodified against the shared <see cref="AzureClusterFixture"/>.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class CommandPipelineEndToEndTests
    : CommandPipelineEndToEndScenarios<AzureClusterFixture>
{
    public CommandPipelineEndToEndTests(AzureClusterFixture fixture) : base(fixture)
    {
    }
}
