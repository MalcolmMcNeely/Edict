using Edict.Azure.Persistence.TableStorage;

namespace Edict.Azure.Tests;

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

        var edictEvent = new AzureRecoverableOrderPlacedEvent(orderId) with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync("AzureRecoverableOrders", edictEvent);

        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);
        var rowDuringCrashWindow = await repository.GetAsync(orderId.ToString(), orderId.ToString());

        AzureControllableUpsertRowExecutor.ShouldFail = false;
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await probe.ForceDrainViaReminderAsync();
        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 0);

        // At-least-once redelivery: the dedup ring (committed atomically with
        // the outbox) suppresses re-handling, so no second UpsertRow is staged.
        await publisher.PublishAsync("AzureRecoverableOrders", edictEvent);
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
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
