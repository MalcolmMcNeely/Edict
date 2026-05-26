using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Azure.Tests.DeadLetter;

[Collection(AzureClusterCollection.Name)]
public sealed class AddEdictAzureDeadLetterRepositoryWiringTests(AzureClusterFixture fixture)
{
    [Fact]
    public void AddEdictAzureDeadLetterRepository_ShouldResolveAzureBackedFacade()
    {
        var repo = fixture.Cluster.Client.ServiceProvider
            .GetRequiredService<IEdictDeadLetterRepository>();

        Assert.IsType<TableBackedDeadLetterRepository>(repo);
    }
}
