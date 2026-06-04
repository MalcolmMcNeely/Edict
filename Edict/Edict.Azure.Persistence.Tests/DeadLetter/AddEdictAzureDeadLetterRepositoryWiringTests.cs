using Edict.Azure.Persistence.Tests.Recovery;
using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Azure.Persistence.Tests.DeadLetter;

// Rides the recovery fixture because that is the one persistence-axis fixture
// wiring the full real-Azure stack — including the table-backed dead-letter
// repository facade this asserts resolves.

[Collection(AzurePersistenceRecoveryCollection.Name)]
public sealed class AddEdictAzureDeadLetterRepositoryWiringTests(AzurePersistenceRecoveryFixture fixture)
{
    [Fact]
    public void AddEdictAzureDeadLetterRepository_ShouldResolveAzureBackedFacade()
    {
        var repo = fixture.Cluster.Client.ServiceProvider
            .GetRequiredService<IEdictDeadLetterRepository>();

        Assert.IsType<TableBackedDeadLetterRepository>(repo);
    }
}
