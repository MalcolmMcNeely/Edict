using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Xunit;

namespace Edict.Testing.Tests;

/// <summary>
/// Drain contract under chaos: every event the harness publishes must reach
/// every subscriber's handler before <see cref="EdictTestApp.Drain"/> returns.
/// The projection's row accumulates per-type invocation counters so the
/// assertion is invariant under chaos's per-subscriber reordering; only the
/// total handler count is asserted. Failures here mean Drain returned while a
/// chaos-held arrival was still unflushed (or a fire-and-forget dispatch task
/// faulted silently).
/// </summary>
public sealed class DrainCoversEveryDispatchTests
{
    [Fact]
    public async Task SinglePublish_TwoEvents_BothHandlersRun()
    {
        var widgetId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DrainCoversEveryDispatchTests).Assembly));

        await app.SendAsync(new PlaceTrackerCommand(widgetId));
        await app.SendAsync(new IncrementTrackerCommand(widgetId));
        await app.Drain();

        var row = await app.GetProjectionRow<TrackerRow>(
            tableName: "trackerinvocations",
            partitionKey: widgetId.ToString(),
            rowKey: "tracker");

        Assert.NotNull(row);
        Assert.Equal(1, row.PlacedHandlerCount);
        Assert.Equal(1, row.IncrementHandlerCount);
    }

    [Fact]
    public async Task ManyEvents_AllHandlersRun_OrderInvariant()
    {
        var widgetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DrainCoversEveryDispatchTests).Assembly));

        await app.SendAsync(new PlaceTrackerCommand(widgetId));
        await app.SendAsync(new IncrementTrackerCommand(widgetId));
        await app.SendAsync(new IncrementTrackerCommand(widgetId));
        await app.SendAsync(new IncrementTrackerCommand(widgetId));
        await app.SendAsync(new IncrementTrackerCommand(widgetId));
        await app.Drain();

        var row = await app.GetProjectionRow<TrackerRow>(
            tableName: "trackerinvocations",
            partitionKey: widgetId.ToString(),
            rowKey: "tracker");

        Assert.NotNull(row);
        Assert.Equal(1, row.PlacedHandlerCount);
        Assert.Equal(4, row.IncrementHandlerCount);
    }

    [Fact]
    public async Task SagaCascade_AllHandlersRun_OrderInvariant()
    {
        var widgetId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DrainCoversEveryDispatchTests).Assembly));

        // Place → Increment → Finalize → saga reacts → SendCascaded → cascade event
        await app.SendAsync(new PlaceTrackerCommand(widgetId));
        await app.SendAsync(new IncrementTrackerCommand(widgetId));
        await app.SendAsync(new FinalizeTrackerCommand(widgetId));
        await app.Drain();

        var row = await app.GetProjectionRow<TrackerRow>(
            tableName: "trackerinvocations",
            partitionKey: widgetId.ToString(),
            rowKey: "tracker");

        Assert.NotNull(row);
        Assert.Equal(1, row.PlacedHandlerCount);
        Assert.Equal(1, row.IncrementHandlerCount);
        Assert.Equal(1, row.FinalizedHandlerCount);
        Assert.Equal(1, row.CascadedHandlerCount);
    }
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.TrackerState")]
public sealed class TrackerState : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public sealed partial record PlaceTrackerCommand(Guid WidgetId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

public sealed partial record IncrementTrackerCommand(Guid WidgetId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

public sealed partial record FinalizeTrackerCommand(Guid WidgetId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

public sealed partial record CascadeTrackerCommand(Guid WidgetId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

[EdictStream("Trackers")]
public sealed partial record TrackerPlacedEvent(Guid WidgetId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

[EdictStream("Trackers")]
public sealed partial record TrackerIncrementedEvent(Guid WidgetId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

[EdictStream("Trackers")]
public sealed partial record TrackerFinalizedEvent(Guid WidgetId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

[EdictStream("Trackers")]
public sealed partial record TrackerCascadedEvent(Guid WidgetId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

public partial class TrackerAggregate : EdictCommandHandler<TrackerState>
{
    public Task<EdictCommandResult> HandleAsync(PlaceTrackerCommand command)
    {
        Raise(new TrackerPlacedEvent(command.WidgetId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> HandleAsync(IncrementTrackerCommand command)
    {
        Raise(new TrackerIncrementedEvent(command.WidgetId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> HandleAsync(FinalizeTrackerCommand command)
    {
        Raise(new TrackerFinalizedEvent(command.WidgetId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> HandleAsync(CascadeTrackerCommand command)
    {
        Raise(new TrackerCascadedEvent(command.WidgetId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.TrackerSagaProgress")]
public sealed class TrackerSagaProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

public partial class TrackerSaga : Edict.Core.Sagas.EdictSaga<TrackerSagaProgress>
{
    public Task HandleAsync(TrackerFinalizedEvent edictEvent)
    {
        Progress.Handled++;
        Dispatch(new CascadeTrackerCommand(edictEvent.WidgetId));
        return Task.CompletedTask;
    }
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.TrackerRow")]
public sealed class TrackerRow : IEdictPersistedState
{
    [Id(0)]
    public int PlacedHandlerCount { get; set; }

    [Id(1)]
    public int IncrementHandlerCount { get; set; }

    [Id(2)]
    public int FinalizedHandlerCount { get; set; }

    [Id(3)]
    public int CascadedHandlerCount { get; set; }
}

public sealed partial class TrackerProjectionBuilder : EdictTableProjectionBuilder<TrackerRow>
{
    public TrackerProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "trackerinvocations";

    protected override string GetRowKey(EdictEvent edictEvent) => "tracker";

    public Task HandleAsync(TrackerPlacedEvent edictEvent)
    {
        CurrentRow.PlacedHandlerCount++;
        return Task.CompletedTask;
    }

    public Task HandleAsync(TrackerIncrementedEvent edictEvent)
    {
        CurrentRow.IncrementHandlerCount++;
        return Task.CompletedTask;
    }

    public Task HandleAsync(TrackerFinalizedEvent edictEvent)
    {
        CurrentRow.FinalizedHandlerCount++;
        return Task.CompletedTask;
    }

    public Task HandleAsync(TrackerCascadedEvent edictEvent)
    {
        CurrentRow.CascadedHandlerCount++;
        return Task.CompletedTask;
    }
}
