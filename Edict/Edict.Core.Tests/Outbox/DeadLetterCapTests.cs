using Edict.Contracts.Commands;
using Edict.Core.Commands;
using Edict.Core.Tests.Grains;

namespace Edict.Core.Tests.Outbox;

// Block-intake, command arm (ADR 0019): once the DeadLetter slice hits the cap
// the grain blocks intake — a subsequent command surfaces an INFRASTRUCTURE
// fault (a thrown EdictOutboxSaturatedException, never a business Rejected) so
// nothing is silently dropped until an operator redrives. In-memory cluster,
// virtual clock, no Azurite (ADR 0016).
[Collection(DeadLetterCapClusterCollection.Name)]
public sealed class DeadLetterCapTests(DeadLetterCapClusterFixture fixture)
{
    [Fact]
    public async Task Send_ShouldThrowInfrastructureFault_WhenDeadLetterCapReached()
    {
        var counterId = Guid.NewGuid();
        ControllableOutboxExecutor.FailedAttempts = 0;
        ControllableOutboxExecutor.ShouldFail = true;
        try
        {
            // First command commits, then its inline drain fails; MaxAttempts=1
            // dead-letters the entry immediately, filling the cap-of-1 slice.
            // The post-commit failure does not surface — Send is still Accepted.
            var first = await fixture.Sender.Send(new IncrementCounterCommand(counterId));
            Assert.IsType<EdictCommandResult.Accepted>(first);

            // Intake is now blocked: the next command to the SAME aggregate
            // throws an infrastructure fault rather than returning Rejected.
            await Assert.ThrowsAsync<EdictOutboxSaturatedException>(
                () => fixture.Sender.Send(new IncrementCounterCommand(counterId)));
        }
        finally
        {
            ControllableOutboxExecutor.ShouldFail = false;
        }
    }
}
