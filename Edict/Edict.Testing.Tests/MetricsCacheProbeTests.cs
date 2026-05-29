using Xunit;

namespace Edict.Testing.Tests;

/// <summary>
/// Acceptance probe for the silo-local metrics cache: the
/// <see cref="EdictTestApp.GetOutboxState"/> and
/// <see cref="EdictTestApp.GetSagaState"/> surfaces read the same cache the
/// silo's <c>OutboxHost</c> + <c>EdictSaga</c> push to, so consumer tests can
/// assert metric-shape state without attaching a <c>MeterListener</c>.
/// </summary>
public sealed class MetricsCacheProbeTests
{
    [Fact]
    public async Task GetOutboxState_ShouldReturnZeroAndNullOldest_AfterCleanDrain()
    {
        var widgetId = Guid.NewGuid();

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(MetricsCacheProbeTests).Assembly));

        await app.Send(new PlaceWidgetCommand(widgetId));
        await app.Drain();

        var (totalPending, oldestEnqueuedAt) =
            app.GetOutboxState(typeof(WidgetAggregate).FullName!);

        Assert.Equal(0, totalPending);
        Assert.Null(oldestEnqueuedAt);
    }
}
