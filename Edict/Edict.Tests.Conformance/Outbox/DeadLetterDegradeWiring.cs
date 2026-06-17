using Edict.Contracts.Events;
using Edict.Core.Commands;
using Edict.Core.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Orleans;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// Silo wiring that arms the three <c>DeadLetterPromoter.Promote()</c>
/// degrade-arm causes so a real-backend drain reaches each one. A fixture turns
/// this on; one call swaps in the failing executors and a superset route
/// resolver, then a scenario stages the matching poisoned outbox entry and
/// drains it through the existing reminder probe.
/// </summary>
public static class DeadLetterDegradeWiring
{
    public static void Wire(IServiceCollection services)
    {
        var publish = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IOutboxEffectExecutor)
            && descriptor.ImplementationType == typeof(PublishEventExecutor));
        services.Remove(publish);
        services.AddSingleton<IOutboxEffectExecutor>(serviceProvider =>
            new UnserialisableForensicBodyPublishExecutor(serviceProvider));

        var send = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IOutboxEffectExecutor)
            && descriptor.ImplementationType == typeof(SendCommandExecutor));
        services.Remove(send);
        services.AddSingleton<IOutboxEffectExecutor, FailingSendCommandExecutor>();

        services.AddSingleton<IOutboxEffectExecutor, UnsupportedKindFailingExecutor>();

        // The promoter's missing-route-key arm only runs once GetRoute resolves
        // the command's owning grain — an unrouted command throws instead and
        // degrades through the serialization arm. Register a superset resolver
        // carrying a route for the route-key-less command so GetRoute succeeds
        // and the key extraction is what fails. The silo's real routes are a
        // subset (this is the only handler-bearing assembly the test silo loads),
        // so normal sends keep routing.
        var routes = new Dictionary<Type, CommandRoute>(
            RouteDiscovery.Discover([typeof(CounterAggregate).Assembly], requireAttribute: false, NullLogger.Instance))
        {
            [typeof(MissingRouteKeyCommand)] = new CommandRoute(
                typeof(MissingRouteKeyCommand),
                typeof(IMissingRouteKeyTarget),
                "Edict.Tests.Conformance.Outbox.MissingRouteKeyHandler",
                _ => string.Empty),
        };
        services.AddSingleton(new CommandRouteResolver(routes));
    }

    interface IMissingRouteKeyTarget;
}

// PublishEvent executor that fails only the unserialisable forensic-body event,
// so that entry exhausts its attempts and promotes, while every other publish —
// crucially the synthetic dead-letter row the promoter emits — passes through to
// the real executor. A global publish fault would also sink the dead-letter row
// and the scenario could never observe it land.
sealed class UnserialisableForensicBodyPublishExecutor : IOutboxEffectExecutor
{
    readonly PublishEventExecutor _inner;
    readonly Serializer _serializer;

    public UnserialisableForensicBodyPublishExecutor(IServiceProvider serviceProvider)
    {
        _inner = ActivatorUtilities.CreateInstance<PublishEventExecutor>(serviceProvider);
        _serializer = serviceProvider.GetRequiredService<Serializer>();
    }

    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
    {
        var edictEvent = liveWireEvent ?? _serializer.Deserialize<EdictEvent>(entry.Payload);
        if (edictEvent is UnserialisableForensicBodyEvent)
        {
            throw new InvalidOperationException("controllable publish failure (unserialisable forensic body)");
        }

        return _inner.ExecuteAsync(entry, streamProvider, deferredDispatch, consumerType, liveWireEvent);
    }
}

// Always-failing SendCommand executor so a staged route-key-less SendCommand
// entry exhausts its attempts and promotes, driving the missing-route-key arm.
sealed class FailingSendCommandExecutor : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.SendCommand;

    public Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent) =>
        throw new InvalidOperationException("controllable send-command failure (missing route key)");
}

// Executor registered under an out-of-range OutboxEffectKind so a staged entry of
// that kind has a drain executor (the host's keyed lookup would otherwise throw
// before promotion) that fails, exhausts its attempts, and promotes through the
// unsupported-kind arm.
sealed class UnsupportedKindFailingExecutor : IOutboxEffectExecutor
{
    public const OutboxEffectKind Kind = (OutboxEffectKind)99;

    OutboxEffectKind IOutboxEffectExecutor.Kind => Kind;

    public Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent) =>
        throw new InvalidOperationException("controllable failure (unsupported effect kind)");
}
