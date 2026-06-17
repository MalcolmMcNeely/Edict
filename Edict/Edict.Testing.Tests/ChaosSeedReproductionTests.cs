using Xunit;

namespace Edict.Testing.Tests;

/// <summary>
/// Proves the reproduce loop: with <c>EDICT_CHAOS_SEED</c> pinned, the same
/// workflow driven through two independent <see cref="EdictTestApp"/> instances
/// produces an identical <see cref="Timeline"/> — same duplicates, same reorder.
/// This is what makes "copy the printed seed into the env var to reproduce a
/// failing interleaving" actually work. Shares the serialized chaos-seed
/// collection so its env/memo manipulation never races a parallel reader; reuses
/// the reorder-prone Widget consumer defined in
/// <see cref="ReorderChaosHarnessTests"/> so chaos has something to shuffle.
/// </summary>
[Collection(ChaosSeedCollection.Name)]
public sealed class ChaosSeedReproductionTests
{
    [Fact]
    public async Task SameSeed_ReproducesIdenticalTimeline()
    {
        // Arrange
        using var scope = new ChaosSeedScope(0xC0FFEE);
        var widgetId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        // Act
        var first = await DriveWorkflowAsync(widgetId);
        var second = await DriveWorkflowAsync(widgetId);

        // Assert
        Assert.Equal(Render(first), Render(second));
    }

    static async Task<Timeline> DriveWorkflowAsync(Guid widgetId)
    {
        await using var app = await EdictTestApp.StartAsync(builder => builder
            .WithConsumer(typeof(ChaosSeedReproductionTests).Assembly));

        await app.SendAsync(new PlaceWidgetCommand(widgetId));
        await app.SendAsync(new IncrementWidgetCommand(widgetId));
        await app.SendAsync(new IncrementWidgetCommand(widgetId));
        await app.SendAsync(new IncrementWidgetCommand(widgetId));
        await app.Drain();

        return app.Timeline;
    }

    // Collapse each timeline entry to its ordered (Kind, Type) identity: the
    // sequence captures both reorder (position) and duplicate redelivery (length),
    // which is exactly what must match across two runs of the same seed.
    static IReadOnlyList<string> Render(Timeline timeline) =>
        timeline.Entries.Select(entry => $"{entry.Kind}|{entry.Type}").ToList();
}
