using System.Diagnostics;

using Edict.Contracts.Configuration;
using Edict.Telemetry;

using Microsoft.Extensions.Options;

using Xunit;

namespace Edict.Testing.Tests;

/// <summary>
/// The in-proc harness must stamp the publish span exactly as production does on
/// the recovery path: a drained entry with no live event reference (the
/// dead-letter-raised tail entry the engine appends, rehydrated from durable
/// state) publishes under its own trace root that links back to the staging
/// command, never as a child of the closed staging turn. Letting the harness
/// diverge from production here would defeat its purpose as the consumer-facing
/// stand-in for the real executor.
/// </summary>
public sealed class HarnessRecoveryPublishSpanTests
{
    [Fact]
    public async Task DeadLetterRaisedPublish_IsRootLinkingToStaging_OnNoLiveRefDrain()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (stopped) { stopped.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);

        var widgetId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

        // OutboxMaxAttempts = 1 dead-letters the failing event-handler effect on its
        // first attempt, so the engine appends an EdictDeadLetterRaised PublishEvent
        // entry and drains it in the same pass with no live ref in hand.
        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(HarnessRecoveryPublishSpanTests).Assembly)
            .Replace<IOptions<EdictOptions>>(Options.Create(new EdictOptions { OutboxMaxAttempts = 1 })));

        await app.SendAsync(new TriggerPoisonCommand(widgetId));
        await app.Drain();

        // ActivityStopped fires as each executor's using-scope unwinds, which can
        // lag the visible drain completion by a tick.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        List<Activity> deadLetterPublishes;
        lock (stopped)
        {
            deadLetterPublishes = stopped
                .Where(a => a.OperationName == $"{SemanticConventions.Events.Spans.Publish} EdictDeadLetterRaised")
                .ToList();
        }

        // The invariant holds for every dead-letter-raised publish the listener saw
        // (this assembly runs its fault tests in parallel), so assert it over all of
        // them rather than racing on a single match.
        Assert.NotEmpty(deadLetterPublishes);
        Assert.All(deadLetterPublishes, publish =>
        {
            Assert.Equal(default, publish.ParentSpanId);
            Assert.Equal(ActivityKind.Producer, publish.Kind);
            var onlyLink = Assert.Single(publish.Links);
            Assert.NotEqual(default, onlyLink.Context.TraceId);
            Assert.NotEqual(publish.TraceId, onlyLink.Context.TraceId);
        });
    }
}
