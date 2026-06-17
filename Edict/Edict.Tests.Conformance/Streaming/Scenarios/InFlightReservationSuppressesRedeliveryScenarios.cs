using System.Diagnostics.Metrics;

using Edict.Telemetry;
using Edict.Tests.Conformance.EventHandler;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance for the in-flight reservation — the literal TOCTOU
/// fix that closes the check-then-dispatch window a redelivery of a still-in-flight
/// id would otherwise slip through. <see cref="EventHandlerDedupsWithinRingScenarios{TFixture}"/>
/// settles the first delivery before redelivering, so its duplicate hits the
/// <em>committed window</em>; this scenario holds the first delivery open — by
/// reserving the id's in-flight slot and never committing it — so the redelivery
/// hits the <em>reservation</em> instead, and proves the suppression is tagged
/// <c>in_flight</c> rather than <c>window</c>. Reserving the slot directly is the only
/// deterministic way to reach this branch: a non-reentrant activation queues a second
/// real delivery behind the first, so a genuinely parked handler would only ever yield
/// a committed-window hit, never an in-flight one. The redelivery still arrives over
/// the real stream, so the suppression and its reason tag are proven end to end.
/// </summary>
public abstract class InFlightReservationSuppressesRedeliveryScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected InFlightReservationSuppressesRedeliveryScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Redelivery_OfAnInFlightEventId_IsSuppressedWithReasonInFlight()
    {
        var customerId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IEmailEventPublisher>(customerId);
        // Same key as the publisher's stream so the implicit-subscription consumer and
        // this probe are one activation, sharing the in-flight reservation set.
        var handler = _fixture.GrainFactory.GetGrain<IEmailHandlerProbe>(customerId);
        var inFlightId = Guid.NewGuid();

        var reasons = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.Idempotency.Meters.DuplicateCount)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? eventType = null;
            string? reason = null;
            foreach (var tag in tags)
            {
                if (tag.Key == SemanticConventions.Events.Tags.Type)
                {
                    eventType = tag.Value as string;
                }
                else if (tag.Key == SemanticConventions.Idempotency.Tags.Reason)
                {
                    reason = tag.Value as string;
                }
            }
            if (eventType == nameof(CustomerNotifiedEvent) && reason is not null)
            {
                lock (reasons) { reasons.Add(reason); }
            }
        });
        listener.Start();

        // Arrange — reserve the id's in-flight slot and leave it held, standing in for a
        // first delivery still parked mid-turn ahead of its commit.
        await handler.ClaimInFlightAsync(inFlightId);

        // Act — redeliver the same id over the real stream. It reaches the held
        // reservation, is suppressed, and the suppression span stops only after the
        // duplicate-count hit is recorded, so waiting on the span settles the metric.
        using var capture = new SpanCapture();
        await publisher.PublishAsync(new CustomerNotifiedEvent(customerId, "in-flight-redelivery") with
        {
            EventId = inFlightId,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Deduplicated} CustomerNotifiedEvent",
            "deduplicated span for the in-flight redelivery");

        // Assert — the suppression was tagged in_flight (reserved but uncommitted), not
        // window (already in the committed dedup window), and the handler never ran the id.
        string capturedReason;
        lock (reasons)
        {
            capturedReason = Assert.Single(reasons);
        }
        Assert.Equal(SemanticConventions.Idempotency.Tags.ReasonValues.InFlight, capturedReason);
        Assert.Empty(await handler.GetHandledEventIdsAsync());
    }
}
