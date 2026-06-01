using Edict.Contracts.Commands;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.EventHandler;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Microsoft.Extensions.Options;

using Orleans.Storage;

using Xunit;

namespace Edict.Testing.Tests;

/// <summary>
/// The harness dispatches to subscribers on fire-and-forget tasks. A fault on
/// one of those tasks — a saga or projection-builder <c>HandleAsync</c> throw,
/// which propagates out of the grain call with the EventId uncommitted — must
/// surface from <see cref="EdictTestApp.Drain"/> instead of vanishing onto the
/// unobserved task while Drain reports success. Otherwise the consumer debugs a
/// null projection row instead of the real cause.
/// </summary>
public sealed class DrainSurfacesDispatchFaultsTests
{
    [Fact]
    public async Task ProjectionHandleThrows_DrainRethrowsTheOriginalException()
    {
        var widgetId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DrainSurfacesDispatchFaultsTests).Assembly));

        await app.SendAsync(new TriggerBoomCommand(widgetId));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Drain());
        Assert.Equal(BoomProjectionBuilder.RefusalMessage, exception.Message);
    }

    [Fact]
    public async Task EventHandlerHandleThrows_DrainDoesNotRethrow_AndRoutesToDeadLetter()
    {
        var widgetId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

        // OutboxMaxAttempts = 1 dead-letters the deferred InvokeHandler effect on
        // its first failure, so the workflow settles in one drain pass with no
        // clock advance. The event handler's throw is engine-caught on that
        // deferred path, never on the fire-and-forget dispatch seam, so it must
        // not reach the rethrow the projection test exercises.
        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DrainSurfacesDispatchFaultsTests).Assembly)
            .Replace<IOptions<EdictOptions>>(Options.Create(new EdictOptions { OutboxMaxAttempts = 1 })));

        await app.SendAsync(new TriggerPoisonCommand(widgetId));
        await app.Drain();

        var deadLettered = app.Timeline.Entries.Any(entry =>
            entry.Kind == "Invocation"
            && entry.Type == nameof(PoisonRaisedEvent)
            && Equals(entry.Data["Outcome"], "DeadLettered"));
        Assert.True(deadLettered);
    }

    [Fact]
    public async Task NonSettlingDrain_ThrowsTimeoutException_NamingTheOutstandingWork()
    {
        var widgetId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

        // Default OutboxMaxAttempts with no AdvanceClock: the deferred effect
        // throws, backs off, and sits pending forever, so the engine never
        // quiesces. The short overload asserts the timeout path in seconds.
        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DrainSurfacesDispatchFaultsTests).Assembly));

        await app.SendAsync(new TriggerPoisonCommand(widgetId));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            app.Drain(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(500)));
        Assert.Contains("pending outbox entries 1", exception.Message);
    }

    [Fact]
    public async Task ExhaustedInconsistentStateRetry_IsSurfaced_NotSwallowed()
    {
        var widgetId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DrainSurfacesDispatchFaultsTests).Assembly));

        await app.SendAsync(new TriggerRaceCommand(widgetId));

        await Assert.ThrowsAsync<InconsistentStateException>(() => app.Drain());
    }
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.BoomState")]
public sealed class BoomState : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public sealed partial record TriggerBoomCommand(Guid WidgetId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

[EdictStream("Booms")]
public sealed partial record BoomTriggeredEvent(Guid WidgetId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

public partial class BoomAggregate : EdictCommandHandler<BoomState>
{
    public Task<EdictCommandResult> HandleAsync(TriggerBoomCommand command)
    {
        Raise(new BoomTriggeredEvent(command.WidgetId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.BoomRow")]
public sealed class BoomRow : IEdictPersistedState
{
    [Id(0)]
    public int Hits { get; set; }
}

public sealed partial class BoomProjectionBuilder : EdictTableProjectionBuilder<BoomRow>
{
    public const string RefusalMessage = "BoomProjectionBuilder refused to handle the event.";

    public BoomProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "boomrows";

    protected override string GetRowKey(EdictEvent edictEvent) => "boom";

    public Task HandleAsync(BoomTriggeredEvent edictEvent) =>
        throw new InvalidOperationException(RefusalMessage);
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.PoisonState")]
public sealed class PoisonState : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public sealed partial record TriggerPoisonCommand(Guid WidgetId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

[EdictStream("Poisons")]
public sealed partial record PoisonRaisedEvent(Guid WidgetId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

public partial class PoisonAggregate : EdictCommandHandler<PoisonState>
{
    public Task<EdictCommandResult> HandleAsync(TriggerPoisonCommand command)
    {
        Raise(new PoisonRaisedEvent(command.WidgetId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

// Event handlers run their HandleAsync on the deferred InvokeHandler path, where
// the engine catches, backs off and dead-letters. A throw here must never reach
// the harness's fire-and-forget dispatch fault seam.
public sealed partial class PoisonAuditHandler : EdictEventHandler
{
    public Task HandleAsync(PoisonRaisedEvent edictEvent) =>
        throw new InvalidOperationException("PoisonAuditHandler refused to audit the event.");
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.RaceState")]
public sealed class RaceState : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public sealed partial record TriggerRaceCommand(Guid WidgetId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

[EdictStream("Races")]
public sealed partial record RaceTriggeredEvent(Guid WidgetId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

public partial class RaceAggregate : EdictCommandHandler<RaceState>
{
    public Task<EdictCommandResult> HandleAsync(TriggerRaceCommand command)
    {
        Raise(new RaceTriggeredEvent(command.WidgetId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.RaceRow")]
public sealed class RaceRow : IEdictPersistedState
{
    [Id(0)]
    public int Hits { get; set; }
}

// Always throws the storage race the harness's bounded retry exists for. Every
// retry attempt hits it, so the exhausted retry must surface the fault rather
// than swallow it onto the unobserved dispatch task.
public sealed partial class RaceProjectionBuilder : EdictTableProjectionBuilder<RaceRow>
{
    public RaceProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "racerows";

    protected override string GetRowKey(EdictEvent edictEvent) => "race";

    public Task HandleAsync(RaceTriggeredEvent edictEvent) =>
        throw new InconsistentStateException("simulated storage ETag race that never clears");
}
