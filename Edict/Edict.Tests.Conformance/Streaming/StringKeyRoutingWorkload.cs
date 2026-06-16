using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// The event the string-key routing gate carries. It is a real
/// <c>[EdictStream]</c> domain event so the Kafka provider provisions its topic
/// exactly as production does; the tenant lives in the composed
/// <c>"{tenant}|{guid}"</c> stream <em>key</em>, not the stream name. The
/// <c>[EdictRouteKey]</c> Guid is the route key the gate deliberately does not
/// route on: the publisher composes the string key by hand, simulating what the
/// future keying change will emit.
/// </summary>
[EdictStream("StringKeyRouting")]
public sealed partial record StringKeyRoutingProbe(Guid RouteKey, int Value) : EdictEvent
{
    [EdictRouteKey]
    public Guid RouteKey { get; init; } = RouteKey;

    public int Value { get; init; } = Value;
}

/// <summary>
/// What the implicitly-subscribed projection grain recovered when an event
/// reached it: the primary key the stream-id mapper handed its activation, and
/// the probe value that arrived.
/// </summary>
[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Streaming.StringKeyRoutingCapture")]
public sealed record StringKeyRoutingCapture(
    [property: Id(0)] string ActivatedKey,
    [property: Id(1)] int Value);

/// <summary>
/// A string-keyed dispatch grain. The gate addresses it by a composed
/// <c>"{tenant}|{guid}"</c> key and has it publish to the stream named by its
/// <em>own</em> recovered primary key, so a key the runtime mangled on the way
/// in would publish to the wrong stream and the projection would never see it.
/// </summary>
public interface IStringKeyRoutingPublisher : IGrainWithStringKey
{
    Task PublishProbeAsync(int value);
}

public sealed class StringKeyRoutingPublisher : Grain, IStringKeyRoutingPublisher
{
    public Task PublishProbeAsync(int value) =>
        this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("StringKeyRouting", this.GetPrimaryKeyString()))
            .OnNextAsync(new StringKeyRoutingProbe(Guid.NewGuid(), value));
}

public interface IStringKeyRoutingProjectionAccess : IGrainWithStringKey
{
    Task<StringKeyRoutingCapture?> GetCaptureAsync();
}

/// <summary>
/// A string-keyed grain implicitly subscribed to the gate's stream. When an
/// event lands on a <c>"{tenant}|{guid}"</c> stream the runtime activates this
/// grain keyed by the verbatim stream key; the grain records that key back so a
/// split, defaulted, or mangled mapping is observable.
/// </summary>
[ImplicitStreamSubscription("StringKeyRouting")]
public sealed class StringKeyRoutingProjection : Grain, IStringKeyRoutingProjectionAccess
{
    StringKeyRoutingCapture? _capture;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("StringKeyRouting", this.GetPrimaryKeyString()));
        await stream.SubscribeAsync(
            (item, _) =>
            {
                if (item is StringKeyRoutingProbe probe)
                {
                    _capture = new StringKeyRoutingCapture(this.GetPrimaryKeyString(), probe.Value);
                }
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<StringKeyRoutingCapture?> GetCaptureAsync() =>
        Task.FromResult(_capture);
}
