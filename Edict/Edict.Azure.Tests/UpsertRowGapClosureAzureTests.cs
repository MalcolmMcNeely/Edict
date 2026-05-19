using Edict.Azure.TableStorage;

namespace Edict.Azure.Tests;

/// <summary>
/// ADR 0016 provider conformance proof that ADR 0012's double-apply gap is
/// <b>closed</b> (ADR 0018). Over the real Azure Queue stream + real Azure
/// Table stack (Azurite via Testcontainers): a crash between the ring/outbox
/// commit and the row write leaves no row written; on the recovery drain the
/// <c>UpsertRow</c> effect applies the row, and the dedup ring (committed
/// atomically with the outbox) makes a redelivered event effectively-once — the
/// row is applied exactly once, not double-applied.
/// </summary>
[Collection(AzureUpsertRowRecoveryClusterCollection.Name)]
public sealed class UpsertRowGapClosureAzureTests(AzureUpsertRowRecoveryClusterFixture fixture)
{
    [Fact]
    public async Task UpsertRowEffect_ShouldApplyRowEffectivelyOnce_WhenCrashBetweenCommitAndDrainThenRedelivered()
    {
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        AzureControllableUpsertRowExecutor.ShouldFail = true;

        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureStreamPublisher>(orderId);
        var probe = fixture.Cluster.GrainFactory.GetGrain<IAzureRecoverableProbe>(orderId);
        var repository = new AzureTableRepository<AzureRecoverableOrderRow>(
            fixture.TableServiceClient, "azurerecoverableorderprojection");

        var evt = new AzureRecoverableOrderPlacedEvent(orderId) with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync("AzureRecoverableOrders", evt);

        // Crash window: event handled over the real queue, ring + outbox
        // committed in one write, but the UpsertRow drain threw.
        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);
        var rowDuringCrashWindow = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        // Recovery: row write heals, the Reminder's drain applies the still
        // -pending UpsertRow effect against real Azure Table Storage.
        AzureControllableUpsertRowExecutor.ShouldFail = false;
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await probe.ForceDrainViaReminderAsync();
        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 0);

        // At-least-once redelivery of the SAME event: the persisted dedup ring
        // (committed atomically with the outbox) suppresses re-handling, so no
        // second UpsertRow is staged.
        await publisher.PublishAsync("AzureRecoverableOrders", evt);
        await Task.Delay(TimeSpan.FromSeconds(3));

        var finalRow = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        await Verify(new
        {
            RowDuringCrashWindow = rowDuringCrashWindow is null ? "null (deferred)" : $"OrderCount={rowDuringCrashWindow.OrderCount}",
            FinalRow = finalRow is null ? "null" : $"OrderCount={finalRow.OrderCount}",
            PendingAfterRecovery = $"{await probe.GetPendingOutboxCountAsync()} entries",
        });
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
