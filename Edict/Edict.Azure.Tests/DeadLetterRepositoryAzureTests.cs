using Edict.Contracts.Commands;
using Edict.Core.Administration;
using Edict.Core.DeadLetter;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Azure.Tests;

// ADR 0016/0019 provider conformance: the dead-letter → inspect → redrive path
// against the REAL Azure Queue stream stack on Azurite (Testcontainers). Proves
// the grain-backed IEdictDeadLetterRepository seam and the IEdictDeadLetterAdmin
// redrive work end-to-end with provider serialization, not just in-memory.
[Collection(AzureDeadLetterClusterCollection.Name)]
public sealed class DeadLetterRepositoryAzureTests(AzureDeadLetterClusterFixture fixture)
{
    [Fact]
    public async Task DeadLetterRepository_ShouldInspectAndReflectRedrive_OverAzureQueueStreams()
    {
        var orderId = Guid.NewGuid();
        var grainFactory = fixture.Cluster.Client.ServiceProvider.GetRequiredService<IGrainFactory>();
        var repo = new GrainDeadLetterRepository(
            grainFactory, "Edict.Azure.Tests.AzureOrderCommandHandler");

        AzureControllableOutboxExecutor.ShouldFail = true;
        try
        {
            await fixture.Sender.Send(new AzurePlaceOrderCommand(orderId, "SKU-1"));

            var deadLettered = await repo.ListAsync(orderId.ToString());
            var entry = Assert.Single(deadLettered);
            Assert.Equal("PublishEvent", entry.Kind);
            Assert.False(string.IsNullOrEmpty(entry.Reason));

            // Downstream heals; operator redrives — the entry republishes over
            // the real Azure queue and the read seam reflects the empty slice.
            AzureControllableOutboxExecutor.ShouldFail = false;
            var admin = grainFactory.GetGrain<IEdictDeadLetterAdmin>(
                orderId, "Edict.Azure.Tests.AzureOrderCommandHandler");
            await admin.RedriveAsync(entry.EntryId);

            Assert.Empty(await repo.ListAsync(orderId.ToString()));
        }
        finally
        {
            AzureControllableOutboxExecutor.ShouldFail = false;
        }
    }
}
