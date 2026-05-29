using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.DeadLetter;

/// <summary>
/// When the outbox catch path sees an <see cref="EdictUnregisteredTypeException"/>,
/// the dead-letter row must carry the typed exception's name in
/// <see cref="EdictDeadLetterEntry.ExceptionType"/> — not the bare
/// <c>System.InvalidOperationException</c> the row used to record. The
/// classifier-to-bucket mapping (Wiring) is exercised by the unit tests
/// alongside this fixture; the row only exposes the type name.
/// </summary>
public abstract class UnregisteredTypePromotesToDeadLetterScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected UnregisteredTypePromotesToDeadLetterScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Promotes_ShouldNameTypedExceptionOnRow()
    {
        var counterId = Guid.NewGuid();
        ControllableOutboxExecutor.Reset();
        ControllableOutboxExecutor.FailureKind = ControllableFailureKind.UnregisteredEvent;
        ControllableOutboxExecutor.ShouldFail = true;

        await _fixture.Sender.Send(new IncrementCounterCommand(counterId));

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
            EdictDeadLetterProjectionBuilder.DeadLetterPartition);

        await WaitUntilAsync(async () =>
        {
            var entries = await deadLetterTable.QueryPartitionAsync(
                EdictDeadLetterProjectionBuilder.DeadLetterPartition);
            return entries.Any(e => e.SourceGrainKey.Contains(counterId.ToString()));
        });

        var allEntries = await deadLetterTable.QueryPartitionAsync(
            EdictDeadLetterProjectionBuilder.DeadLetterPartition);
        var entry = allEntries.Single(e => e.SourceGrainKey.Contains(counterId.ToString()));

        Assert.Equal(typeof(EdictUnregisteredTypeException).FullName, entry.ExceptionType);
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
