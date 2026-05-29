using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Xunit;

namespace Edict.Testing.Tests;

/// <summary>
/// End-to-end proof that the in-process harness applies bounded reorder chaos
/// on every test run — a redelivered older event can land behind a newer one
/// of the same aggregate, so consumers must tolerate it. A throwaway in-test
/// consumer — one aggregate plus a "reset on Place" projection — surfaces
/// the property without depending on any sample-app types.
/// </summary>
public sealed class ReorderChaosHarnessTests
{
    [Fact]
    public async Task ReorderFragileProjection_LandsZero_UnderDefaultChaos()
    {
        var widgetId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(ReorderChaosHarnessTests).Assembly));

        await app.Send(new PlaceWidgetCommand(widgetId));
        await app.Send(new IncrementWidgetCommand(widgetId));
        await app.Drain();

        var row = await app.GetProjectionRow<WidgetCounterRow>(
            tableName: "widgetcounter",
            partitionKey: widgetId.ToString(),
            rowKey: "counter");

        // Strict order would leave Count = 1; reorder lands WidgetPlaced after
        // WidgetIncremented, so WidgetPlaced's Count = 0 reset wins last.
        Assert.NotNull(row);
        Assert.Equal(0, row.Count);
    }
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.WidgetState")]
public sealed class WidgetState : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public sealed partial record PlaceWidgetCommand(Guid WidgetId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

public sealed partial record IncrementWidgetCommand(Guid WidgetId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

[EdictStream("Widgets")]
public sealed partial record WidgetPlacedEvent(Guid WidgetId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

[EdictStream("Widgets")]
public sealed partial record WidgetIncrementedEvent(Guid WidgetId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WidgetId { get; init; } = WidgetId;
}

public partial class WidgetAggregate : EdictCommandHandler<WidgetState>
{
    public Task<EdictCommandResult> Handle(PlaceWidgetCommand command)
    {
        State.Count = 0;
        Raise(new WidgetPlacedEvent(command.WidgetId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(IncrementWidgetCommand command)
    {
        State.Count++;
        Raise(new WidgetIncrementedEvent(command.WidgetId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.DecoyRow")]
public sealed class DecoyRow : IEdictPersistedState
{
    [Id(0)]
    public int Hits { get; set; }
}

// Declared first so SubscriberMap iterates it before the fragile projection.
// Soaks the seed's leading hold rolls, leaving the fragile projection's
// Increment arrival on a no-hold roll — Increment dispatches immediately,
// Place ends up flushed last, Count goes to 0.
public sealed partial class DecoyWidgetProjectionBuilder : EdictTableProjectionBuilder<DecoyRow>
{
    public DecoyWidgetProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "widgetdecoy";

    protected override string GetRowKey(EdictEvent edictEvent) => "decoy";

    public Task Handle(WidgetPlacedEvent edictEvent)
    {
        CurrentRow.Hits++;
        return Task.CompletedTask;
    }

    public Task Handle(WidgetIncrementedEvent edictEvent)
    {
        CurrentRow.Hits++;
        return Task.CompletedTask;
    }
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.WidgetCounterRow")]
public sealed class WidgetCounterRow : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

// "Reset on Place" shape: WidgetPlaced re-initialises Count = 0, WidgetIncremented
// increments. Strict order leaves Count = 1; bounded reorder lands the reset last
// and Count goes to 0 — the fragility the harness must surface.
public sealed partial class WidgetCounterProjectionBuilder : EdictTableProjectionBuilder<WidgetCounterRow>
{
    public WidgetCounterProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "widgetcounter";

    protected override string GetRowKey(EdictEvent edictEvent) => "counter";

    public Task Handle(WidgetPlacedEvent edictEvent)
    {
        CurrentRow.Count = 0;
        return Task.CompletedTask;
    }

    public Task Handle(WidgetIncrementedEvent edictEvent)
    {
        CurrentRow.Count++;
        return Task.CompletedTask;
    }
}
