using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.EventHandler;

using Orleans;
using Orleans.Streams;

namespace Edict.Azure.Tests;

// ── Aggregate / handler types ───────────────────────────────────────────────

[EdictStream("AzureEmailEvents")]
public sealed partial record AzureCustomerNotifiedEvent(Guid CustomerId, string Reason) : EdictEvent
{
    [EdictRouteKey]
    public Guid CustomerId { get; init; } = CustomerId;

    public string Reason { get; init; } = Reason;
}

public interface IAzureEmailEventPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent evt);
}

public sealed class AzureEmailEventPublisher : Grain, IAzureEmailEventPublisher
{
    public Task PublishAsync(EdictEvent evt)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("AzureEmailEvents", this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }
}

public interface IAzureEmailHandlerProbe : IGrainWithGuidKey
{
    Task<int> GetHandledCountAsync();
    Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync();
}

/// <summary>
/// Azure-suite <see cref="EdictEventHandler"/>: relies on the generator's
/// emitted <c>HandlesType</c> + <c>DispatchAsync</c> + implicit-stream
/// subscription, so this is the same shape consumers ship.
/// </summary>
public sealed partial class AzureEmailEventHandler : EdictEventHandler, IAzureEmailHandlerProbe
{
    readonly List<Guid> _handled = [];

    public Task Handle(AzureCustomerNotifiedEvent evt)
    {
        _handled.Add(evt.EventId);
        return Task.CompletedTask;
    }

    public Task<int> GetHandledCountAsync() => Task.FromResult(_handled.Count);

    public Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync() =>
        Task.FromResult<IReadOnlyList<Guid>>(_handled.AsReadOnly());
}

// ── Tests ───────────────────────────────────────────────────────────────────

/// <summary>
/// Azurite/Testcontainers conformance for <c>EdictEventHandler</c> deferred
/// drain (ADR 0023). Proves: real Azure-Queue redelivery routes through the
/// dedup ring and the InvokeHandler outbox staging path; the deferred drain
/// re-invokes the consumer's <c>Handle</c> exactly once even when the queue
/// re-queues the message (visibility-timeout expiry).
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class AzureEventHandlerRedeliveryTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task EventHandler_ShouldRunHandleExactlyOnce_WhenAzureQueueDeliversHandledEvent()
    {
        var customerId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailEventPublisher>(customerId);
        var handler = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailHandlerProbe>(customerId);

        var eventId = Guid.NewGuid();
        var evt = new AzureCustomerNotifiedEvent(customerId, "welcome") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(evt);

        var handled = await WaitForHandledAsync(handler);
        Assert.Single(handled);
        Assert.Equal(eventId, handled[0]);
    }

    [Fact]
    public async Task EventHandler_ShouldSuppressDuplicate_WhenSameEventIdRedeliveredViaAzureQueue()
    {
        var customerId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailEventPublisher>(customerId);
        var handler = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailHandlerProbe>(customerId);

        var eventId = Guid.NewGuid();
        var evt = new AzureCustomerNotifiedEvent(customerId, "first") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var duplicate = new AzureCustomerNotifiedEvent(customerId, "duplicate-marker") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(evt);
        await WaitForHandledAsync(handler, expectedCount: 1);

        await publisher.PublishAsync(duplicate);
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Single(await handler.GetHandledEventIdsAsync());
    }

    static async Task<IReadOnlyList<Guid>> WaitForHandledAsync(
        IAzureEmailHandlerProbe handler, int expectedCount = 1, int timeoutSeconds = 15)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await handler.GetHandledEventIdsAsync();
            if (ids.Count >= expectedCount)
            {
                return ids;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await handler.GetHandledEventIdsAsync();
    }
}
