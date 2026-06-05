using Edict.Testing;

using Sample.Contracts.Settlement.Commands;
using Sample.Domain.Settlement.Sagas;

using Xunit;

namespace Sample.Azure.Silo.Tests.Settlement;

/// <summary>
/// Drives the EdictSchedule saga-parity surface end-to-end through the in-memory
/// Test Framework: a saga schedules a poll from inside its event HandleAsync, each
/// interval-agnostic FireDueSchedulesAsync routes a fire back to
/// HandleAsync(PollGatewayMessage), and a fire can dispatch a Command. The saga
/// schedule is bounded by the saga's own timeout, never the command-handler silo
/// default, and a schedule started with a timeout compensates on its cap.
/// </summary>
public sealed class GatewaySettlementScheduleTests
{
    [Fact]
    public async Task SagaSchedule_FiredUntilSettled_DispatchesTheConfirmingCommand_AndIsUncappedByTheCommandHandlerDefault()
    {
        var paymentId = Guid.Parse("5e000000-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(GatewaySettlementSaga).Assembly));

        // No poll timeout: the schedule is bounded only by the saga's own lifetime.
        await app.SendAsync(new BeginGatewaySettlementCommand(paymentId, PollTimeoutSeconds: 0));
        await app.Drain();

        // A timeout fire is a no-op against the uncapped saga schedule: if the
        // command-handler silo default (7 days) applied inside a saga, this would
        // fire the cap and dispatch the compensating Command.
        await app.FireScheduleTimeoutsAsync();
        Assert.DoesNotContain(app.Timeline.Entries, entry =>
            entry.Kind == "Command" && entry.Type == nameof(AbandonSettlementCommand));

        // Two due fires settle the poll; the second dispatches the confirming Command.
        await app.FireDueSchedulesAsync();
        await app.FireDueSchedulesAsync();

        Assert.Equal(1, app.Timeline.Entries.Count(entry =>
            entry.Kind == "Command" && entry.Type == nameof(ConfirmSettlementCommand)));

        // The schedule completed: a further fire dispatches nothing more.
        await app.FireDueSchedulesAsync();
        Assert.Equal(1, app.Timeline.Entries.Count(entry =>
            entry.Kind == "Command" && entry.Type == nameof(ConfirmSettlementCommand)));

        // The poll count and terminal outcome the Playground surfaces, read off
        // the saga's own durable Progress through the same accessor.
        var progress = await app.GetSagaProgress<GatewaySettlementSaga, GatewaySettlementProgress>(paymentId);

        await Verify(progress);
    }

    [Fact]
    public async Task SagaScheduleTimeout_Fired_DispatchesTheCompensatingCommand()
    {
        var paymentId = Guid.Parse("5e000000-0000-0000-0000-000000000002");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(GatewaySettlementSaga).Assembly));

        // A 1-second poll timeout against the 2-second demo cadence: the cap fires
        // before any due tick, and the poll never settles.
        await app.SendAsync(new BeginGatewaySettlementCommand(paymentId, PollTimeoutSeconds: 1));
        await app.Drain();

        await app.FireScheduleTimeoutsAsync();

        Assert.Equal(1, app.Timeline.Entries.Count(entry =>
            entry.Kind == "Command" && entry.Type == nameof(AbandonSettlementCommand)));

        // The compensation outcome the Playground surfaces off the saga's Progress:
        // the poll never settled, so the count stays zero and the outcome is abandoned.
        var progress = await app.GetSagaProgress<GatewaySettlementSaga, GatewaySettlementProgress>(paymentId);

        await Verify(progress);
    }
}
