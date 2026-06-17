using Xunit;

namespace Edict.Testing.Tests;

/// <summary>
/// Proves the <see cref="EdictTestAppBuilder.WithoutChaos"/> opt-out: a multi-step
/// workflow driven with chaos disabled lands the canonical strict-order outcome no
/// matter the run-wide seed — each event delivered once, in raise order, and the
/// reorder-fragile projection at its strict-order value. The seeds include
/// <c>0xED1C7</c>, which <see cref="ReorderChaosHarnessTests"/> proves corrupts the
/// same projection to zero when chaos is on, so the contrast pins the opt-out's
/// effect rather than a seed that happens to roll no reorder. Reuses the
/// reorder-prone Widget consumer and joins the serialized chaos-seed collection
/// because it pins the process-wide seed for its duration.
/// </summary>
[Collection(ChaosSeedCollection.Name)]
public sealed class WithoutChaosTests
{
    [Theory]
    [InlineData(0xED1C7)]
    [InlineData(0x1)]
    [InlineData(12345)]
    public async Task WithoutChaos_DeliversEachEventOnceInRaiseOrder_RegardlessOfSeed(int seed)
    {
        // Arrange
        using var scope = new ChaosSeedScope(seed);
        var widgetId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        await using var app = await EdictTestApp.StartAsync(builder => builder
            .WithConsumer(typeof(WithoutChaosTests).Assembly)
            .WithoutChaos());

        // Act
        await app.SendAsync(new PlaceWidgetCommand(widgetId));
        await app.SendAsync(new IncrementWidgetCommand(widgetId));
        await app.Drain();

        // Assert
        var eventSequence = app.Timeline.Entries
            .Where(entry => entry.Kind == "Event")
            .Select(entry => entry.Type)
            .ToList();
        Assert.Equal(
            new[] { nameof(WidgetPlacedEvent), nameof(WidgetIncrementedEvent) },
            eventSequence);

        // Strict order leaves the reorder-fragile counter at 1; bounded reorder
        // would land WidgetPlaced's reset last and drop it to 0.
        var row = await app.GetProjectionRow<WidgetCounterRow>(
            tableName: "widgetcounter",
            partitionKey: widgetId.ToString("N"),
            rowKey: "counter");
        Assert.NotNull(row);
        Assert.Equal(1, row.Count);
    }
}
