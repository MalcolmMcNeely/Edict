using Edict.Contracts.Events;
using Edict.Core.Projections;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Grains;

public interface IOrderProjectionAccess : IGrainWithGuidKey
{
    Task<int> GetOrderCountAsync();
}

/// <summary>
/// Test projection grain. The generator adds the grain interface,
/// [ImplicitStreamSubscription], SubscribeToStreamAsync, and DispatchAsync.
/// </summary>
public sealed partial class OrderProjectionBuilder : EdictProjectionBuilder, IOrderProjectionAccess
{
    private int _orderCount;

    public Task<int> GetOrderCountAsync() => Task.FromResult(_orderCount);

    public Task Handle(OrderPlacedEvent evt)
    {
        _orderCount++;
        return Task.CompletedTask;
    }
}

public interface IProjectionPublisherGrain : IGrainWithGuidKey
{
    Task PublishToStreamAsync(string streamName, EdictEvent evt);
}

/// <summary>
/// Test-only grain: publishes any event directly to a named domain stream,
/// bypassing EdictCommandHandler. Used to inject events with known EventIds.
/// </summary>
public sealed class ProjectionPublisherGrain : Grain, IProjectionPublisherGrain
{
    public Task PublishToStreamAsync(string streamName, EdictEvent evt)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create(streamName, this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }
}
