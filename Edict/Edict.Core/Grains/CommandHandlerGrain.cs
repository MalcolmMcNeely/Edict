using System.Diagnostics;
using System.Reflection;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Results;
using Edict.Core.Validation;
using Edict.Telemetry;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Streams;

namespace Edict.Core.Grains;

/// <summary>
/// Base for an aggregate grain. A Command is a direct grain call, so there is
/// deliberately no deduplication here (dedup is for at-least-once stream
/// delivery, which Commands never use — ADR 0004). The consumer writes a
/// <c>partial</c> grain with one strongly typed <c>Handle(TCommand)</c> per
/// command; the source generator emits the matching <see cref="Dispatch"/>
/// override that type-switches to those overloads, calling
/// <see cref="ValidateAndHandleAsync{TCommand}"/> per arm.
/// </summary>
public abstract class EdictCommandHandlerGrain : Grain, IEdictCommandHandler
{
    private List<EdictEvent>? _raisedEvents;

    /// <inheritdoc />
    public abstract Task<EdictCommandResult> Dispatch(EdictCommand command);

    /// <summary>
    /// Buffers an event to be flushed to its domain stream after the current
    /// command returns <c>Accepted</c>. Discarded on <c>Rejected</c> or handler
    /// throw. Stamped with <c>EventId</c>, <c>OccurredAt</c>, and trace context
    /// at flush time (ADR 0011).
    /// </summary>
    protected void Raise(EdictEvent theEvent)
    {
        ArgumentNullException.ThrowIfNull(theEvent);
        (_raisedEvents ??= []).Add(theEvent);
    }

    /// <summary>
    /// Stamps and publishes all buffered events to their domain streams.
    /// Called by the generated <c>Dispatch</c> after <c>Handle</c> returns
    /// <c>Accepted</c> and the validator passed. A publication failure surfaces
    /// as an infrastructure exception to the command caller — no outbox exists
    /// yet, so a dropped event must never be silent.
    /// </summary>
    protected async Task FlushRaisedEventsAsync()
    {
        if (_raisedEvents is null || _raisedEvents.Count == 0)
            return;

        var provider = this.GetStreamProvider("edict");

        // Restore the command span as explicit parent so publish spans are direct children
        // even across the Orleans grain call boundary (ADR 0003).
        var (cmdTraceId, cmdSpanId, cmdTraceState) = ActivityExtensions.ReadRequestContext();
        var parentContext = ActivityExtensions.RestoreFromStrings(cmdTraceId, cmdSpanId, cmdTraceState);

        foreach (var evt in _raisedEvents)
        {
            var (streamName, routeKey) = GetEventStreamAddress(evt);
            var stream = provider.GetStream<EdictEvent>(StreamId.Create(streamName, routeKey));

            using var publishActivity = EdictDiagnostics.ActivitySource.StartEdictEventPublish(
                evt.GetType().Name, parentContext);

            var stamped = evt with
            {
                EventId = Guid.NewGuid(),
                OccurredAt = DateTimeOffset.UtcNow,
                TraceId = publishActivity?.TraceId.ToHexString() ?? cmdTraceId,
                SpanId = publishActivity?.SpanId.ToHexString() ?? cmdSpanId,
                TraceState = publishActivity?.TraceStateString ?? cmdTraceState,
            };

            await stream.OnNextAsync(stamped);
        }

        _raisedEvents = null;
    }

    /// <summary>Discards all buffered events. Called on <c>Rejected</c> or handler throw.</summary>
    protected void DiscardRaisedEvents() => _raisedEvents = null;

    /// <summary>
    /// Resolves <see cref="IValidator{TCommand}"/> from grain DI, runs it with
    /// the current grain state in <c>ValidationContext.RootContextData</c>, and
    /// short-circuits to <see cref="EdictCommandResult.Rejected"/> on failure.
    /// Returns the result of <paramref name="handle"/> when validation passes or
    /// no validator is registered. Called from the generated <c>Dispatch</c>.
    /// </summary>
    protected async Task<EdictCommandResult> ValidateAndHandleAsync<TCommand>(
        TCommand command,
        Func<Task<EdictCommandResult>> handle)
        where TCommand : EdictCommand
    {
        var validator = ServiceProvider.GetService<IValidator<TCommand>>();

        if (validator is not null)
        {
            var context = new ValidationContext<TCommand>(command);
            var state = GetValidationState();
            if (state is not null)
                context.RootContextData[EdictValidationKeys.GrainState] = state;

            var result = await validator.ValidateAsync(context);
            if (!result.IsValid)
            {
                return new EdictCommandResult.Rejected(
                    result.Errors
                        .Select(static e => new EdictRejectionReason(
                            e.ErrorCode ?? "validation_error",
                            e.ErrorMessage))
                        .ToArray());
            }
        }

        return await handle();
    }

    /// <summary>
    /// Override to expose the grain's current state to validators via
    /// <c>ValidationContext.RootContextData[<see cref="EdictValidationKeys.GrainState"/>]</c>.
    /// The default returns <c>null</c> (no state injected).
    /// </summary>
    protected virtual object? GetValidationState() => null;

    private static (string StreamName, Guid RouteKey) GetEventStreamAddress(EdictEvent evt)
    {
        var type = evt.GetType();

        var streamAttr = (EdictStreamAttribute?)Attribute.GetCustomAttribute(type, typeof(EdictStreamAttribute))
            ?? throw new InvalidOperationException(
                $"Event {type.Name} is missing [EdictStream] — every concrete event must declare its domain stream (ADR 0011).");

        var routeKeyProp = Array.Find(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => Attribute.IsDefined(p, typeof(EdictRouteKeyAttribute)))
            ?? throw new InvalidOperationException(
                $"Event {type.Name} is missing a [EdictRouteKey] Guid property (ADR 0011).");

        return (streamAttr.Name, (Guid)routeKeyProp.GetValue(evt)!);
    }
}
