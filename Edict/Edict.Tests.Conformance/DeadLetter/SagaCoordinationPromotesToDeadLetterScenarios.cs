using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.DeadLetter;

/// <summary>
/// Verifies the dead-letter row carries the <see cref="EdictSagaCoordinationException"/>
/// type name when a saga's one-command-per-event coordination is violated.
/// The controllable executor injects the throw at the outbox effect boundary
/// because the saga handler path itself escapes via the Orleans stream
/// subsystem and not the dead-letter pipeline — the path under test is the
/// catch in <c>OutboxHost.ExecuteGroupCapturingAsync</c> and the row the
/// dead-letter projection writes.
/// </summary>
public abstract class SagaCoordinationPromotesToDeadLetterScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected SagaCoordinationPromotesToDeadLetterScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Promotes_ShouldNameTypedExceptionOnRow()
    {
        var counterId = Guid.NewGuid();
        ControllableOutboxExecutor.Reset();
        ControllableOutboxExecutor.FailureKind = ControllableFailureKind.SagaCoordination;
        ControllableOutboxExecutor.ShouldFail = true;

        await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            await probe.ForceDrainViaReminderAsync();
            return ControllableOutboxExecutor.FailedAttempts >= 2;
        });

        ControllableOutboxExecutor.ShouldFail = false;

        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await probe.ForceDrainViaReminderAsync();
            return await probe.GetPendingOutboxCountAsync() == 0;
        });

        var deadLetterTable = _fixture.GetTableRepository<EdictDeadLetterEntry>(
            EdictDeadLetterTable.Name);

        await WaitUntilAsync(async () =>
        {
            var entries = await deadLetterTable.QueryPartitionAsync(
                EdictDeadLetterTable.Name);
            return entries.Any(e => e.SourceGrainKey.Contains(counterId.ToString()));
        });

        var allEntries = await deadLetterTable.QueryPartitionAsync(
            EdictDeadLetterTable.Name);
        var entry = allEntries.Single(e => e.SourceGrainKey.Contains(counterId.ToString()));

        Assert.Equal(typeof(EdictSagaCoordinationException).FullName, entry.ExceptionType);
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }
    }
}
