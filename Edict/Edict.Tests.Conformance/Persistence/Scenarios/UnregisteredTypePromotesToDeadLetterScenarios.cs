using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// When the outbox catch path sees an <see cref="EdictUnregisteredTypeException"/>,
/// the dead-letter row must carry the typed exception's name in
/// <see cref="EdictDeadLetterEntry.ExceptionType"/> — not the bare
/// <c>System.InvalidOperationException</c> the row used to record.
/// </summary>
public abstract class UnregisteredTypePromotesToDeadLetterScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
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
        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.FailureKind = ControllableFailureKind.UnregisteredEvent;
        _fixture.OutboxFault.ShouldFail = true;

        await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            await probe.ForceDrainViaReminderAsync();
            return _fixture.OutboxFault.FailedAttempts >= 2;
        });

        _fixture.OutboxFault.ShouldFail = false;

        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await probe.ForceDrainViaReminderAsync();
            return await probe.GetPendingOutboxCountAsync() == 0;
        });

        var deadLetterTable = _fixture.GetTableStore<EdictDeadLetterEntry>(
            EdictDeadLetterTable.Name);

        await WaitUntilAsync(async () =>
        {
            var entries = await deadLetterTable.QueryPartitionAsync(
                EdictDeadLetterTable.Name);
            return entries.Any(e => e.SourceGrainKey.Contains(counterId.ToString("N")));
        });

        var allEntries = await deadLetterTable.QueryPartitionAsync(
            EdictDeadLetterTable.Name);
        var entry = allEntries.Single(e => e.SourceGrainKey.Contains(counterId.ToString("N")));

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
