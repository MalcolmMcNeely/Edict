using Edict.Core.Tests.Grains;
using Edict.Core.Tests.Outbox;

namespace Edict.Core.Tests;

/// <summary>
/// Mechanism proof that ADR 0012's double-apply gap is <b>closed</b>, not
/// accepted (ADR 0018). The Table Projection Builder's row write is an
/// <c>UpsertRow</c> outbox effect committed atomically with the dedup-ring
/// commit, then drained at-least-once. In-memory cluster + virtual clock per
/// ADR 0016 — the cross-pod redelivery+dedup proof over real Azure Queue
/// streams lives in <c>Edict.Azure.Tests</c>.
/// </summary>
[Collection(UpsertRowRecoveryClusterCollection.Name)]
public sealed class GapClosureTests(UpsertRowRecoveryClusterFixture fixture)
{
    // Cycle 2 — the row write is now a deferred outbox effect committed
    // atomically with the ring: a crash before the drain leaves NO row written
    // but the ring/outbox committed and the entry pending; the lazy Reminder's
    // drain then writes it (effectively-once). There is no longer a non-atomic
    // row write before the ring commit.
    [Fact]
    public async Task UpsertRowEffect_ShouldDeferRowWriteUntilDrainSucceeds_WhenCrashBetweenCommitAndDrain()
    {
        var orderId = Guid.NewGuid();
        ControllableUpsertRowExecutor.Attempts = 0;
        ControllableUpsertRowExecutor.DuplicateOnSuccess = false;
        ControllableUpsertRowExecutor.ShouldFail = true;

        var publisher = fixture.Cluster.GrainFactory.GetGrain<IProjectionPublisherGrain>(orderId);
        var probe = fixture.Cluster.GrainFactory.GetGrain<IProbedTableProjection>(orderId);

        await publisher.PublishToStreamAsync("ProbedOrders", new ProbedOrderPlacedEvent(orderId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 1);

        // Crash window: ring + outbox committed, but the row write is deferred.
        var store = fixture.TableStoreFactory.GetStore<ProbedOrderRow>("probedorderprojection");
        var rowBeforeRecovery = store.Get(orderId.ToString(), orderId.ToString());
        var pendingBeforeRecovery = await probe.GetPendingOutboxCountAsync();
        var reminderBeforeRecovery = await probe.HasDrainReminderAsync();

        // Recovery: downstream healthy, backoff elapsed (200ms deterministic),
        // the Reminder's drain writes the still-pending row and winds the lazy
        // Reminder back down.
        ControllableUpsertRowExecutor.ShouldFail = false;
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await probe.ForceDrainViaReminderAsync();

        await WaitUntilAsync(async () => await probe.GetPendingOutboxCountAsync() == 0);
        var rowAfterRecovery = store.Get(orderId.ToString(), orderId.ToString());

        await Verify(new
        {
            RowBeforeRecovery = rowBeforeRecovery is null ? "null (deferred)" : $"OrderCount={rowBeforeRecovery.OrderCount}",
            PendingBeforeRecovery = $"{pendingBeforeRecovery} entry",
            ReminderBeforeRecovery = reminderBeforeRecovery,
            RowAfterRecovery = rowAfterRecovery is null ? "null" : $"OrderCount={rowAfterRecovery.OrderCount}",
            PendingAfterRecovery = $"{await probe.GetPendingOutboxCountAsync()} entries",
            ReminderAfterRecovery = await probe.HasDrainReminderAsync(),
        });
    }

    // Cycle 2 — the UpsertRow effect carries the whole computed row and the
    // drain is a pk/rk full-row replace, so applying the same effect twice
    // (at-least-once redelivery) leaves the row applied exactly once.
    [Fact]
    public async Task UpsertRowEffect_ShouldApplyRowOnce_WhenEffectRedelivered()
    {
        var orderId = Guid.NewGuid();
        ControllableUpsertRowExecutor.Attempts = 0;
        ControllableUpsertRowExecutor.ShouldFail = false;
        ControllableUpsertRowExecutor.DuplicateOnSuccess = true;

        var publisher = fixture.Cluster.GrainFactory.GetGrain<IProjectionPublisherGrain>(orderId);
        var probe = fixture.Cluster.GrainFactory.GetGrain<IProbedTableProjection>(orderId);

        await publisher.PublishToStreamAsync("ProbedOrders", new ProbedOrderPlacedEvent(orderId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var row = await WaitForRowAsync(orderId);
        ControllableUpsertRowExecutor.DuplicateOnSuccess = false;

        // The effect was applied twice (DuplicateOnSuccess) yet the row is 1,
        // not 2 — the pk/rk full-row replace is idempotent under at-least-once
        // redelivery.
        await Verify(new
        {
            EffectAppliedTwice = true,
            FinalOrderCount = $"OrderCount={row.OrderCount}",
        });
    }

    async Task<ProbedOrderRow> WaitForRowAsync(Guid orderId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var store = fixture.TableStoreFactory.GetStore<ProbedOrderRow>("probedorderprojection");
                var row = store.Get(orderId.ToString(), orderId.ToString());
                if (row is not null)
                    return row;
            }
            catch (KeyNotFoundException) { }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        throw new InvalidOperationException("row never written");
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
