using Edict.Contracts.Commands;
using Edict.Contracts.Sending;

namespace Edict.Azure.Tests;

// Successful fixture initialisation is the proof: TestCluster.DeployAsync
// surfaces any wiring fault (missing service, options validation, lifecycle
// ordering) as an exception, so reaching the assertion means the three
// ISiloBuilder extensions wire consistently with Orleans's silo lifecycle.
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
