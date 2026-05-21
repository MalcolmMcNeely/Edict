using Edict.Contracts.Commands;
using Edict.Contracts.Sending;

namespace Edict.Azure.Tests;

// ADR 0028 conformance: the three new ISiloBuilder calls
// (silo.AddEdict() + silo.AddEdictAzureStreams() + silo.AddEdictAzurePersistence())
// deploy an Orleans silo against a real Azurite. Successful fixture
// initialisation is the proof — TestCluster.DeployAsync surfaces any
// startup-time wiring fault (missing service, options validation, lifecycle
// ordering) as an exception out of the fixture, so reaching the assertion
// below means every Edict-specific registration the extensions emit is
// internally consistent with Orleans's silo lifecycle.
[Collection(EdictAzureSiloBuilderExtensionsClusterCollection.Name)]
public sealed class EdictAzureSiloBuilderExtensionsTests(
    EdictAzureSiloBuilderExtensionsClusterFixture fixture)
{
    [Fact]
    public void SiloBuiltViaNewExtensions_ShouldDeployAgainstAzurite()
    {
        Assert.NotNull(fixture.Cluster);
        Assert.NotNull(fixture.Sender);
    }
}
