using Edict.Contracts;
using Edict.Core.Outbox;

using Orleans;
using Orleans.Runtime;

namespace Edict.Core.Tests.Outbox;

/// <summary>
/// Trivial probe surface used by the <see cref="EdictDurableConsumerBase{TPayload}"/>
/// fixture: seed state, snapshot state, and tick the reminder. The grain itself
/// adds no role-specific behaviour — no commands, no stream observer — so every
/// test invocation exercises only the shared host plumbing under test.
/// </summary>
public interface ITestDurableConsumer : IGrainWithGuidKey
{
    Task SeedOutboxAsync(OutboxSlice slice);
    Task<OutboxSlice> GetOutboxAsync();
    Task TriggerReminderAsync();
    Task DeactivateAndWaitAsync();
}

/// <summary>
/// Trivial subclass of <see cref="EdictDurableConsumerBase{TPayload}"/> with no
/// role-specific surface, used by <c>TestDurableConsumerTests</c> to target the
/// shared host plumbing directly rather than transitively through the two
/// consumer roots (ADR 0018 unified envelope; ADR 0022 forensic-only dead
/// letters).
/// </summary>
public sealed class TestDurableConsumer : EdictDurableConsumerBase<EdictUnit>, ITestDurableConsumer
{
    public async Task SeedOutboxAsync(OutboxSlice slice)
    {
        State.Outbox = slice;
        await WriteStateAsync();
    }

    public Task<OutboxSlice> GetOutboxAsync() => Task.FromResult(State.Outbox);

    public Task TriggerReminderAsync() =>
        ReceiveReminder(DrainReminderName, new TickStatus());

    public Task DeactivateAndWaitAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}
