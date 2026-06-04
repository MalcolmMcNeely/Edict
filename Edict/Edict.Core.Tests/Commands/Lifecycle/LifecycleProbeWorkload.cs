using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;

using FluentValidation;

using MessagePack;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Commands.Lifecycle;

[GenerateSerializer]
[Alias("Edict.Core.Tests.Commands.Lifecycle.LifecycleProbeState")]
public sealed class LifecycleProbeState : IEdictPersistedState
{
    [Id(0)]
    public int Counter { get; set; }
}

// Constructed inside the grain and passed straight to ValidateAndHandleAsync, so
// it never crosses a grain boundary. It carries a route key only to satisfy the
// EDICT003 analyzer.
public sealed partial record LifecycleProbeCommand(Guid Key) : EdictCommand
{
    [EdictRouteKey]
    public Guid Key { get; init; } = Key;
}

// A separate command type so a FluentValidation IValidator<T> can be registered
// for it without colliding with the no-validator path.
public sealed partial record LifecycleValidatedCommand(Guid Key, bool ShouldPass) : EdictCommand
{
    [EdictRouteKey]
    public Guid Key { get; init; } = Key;

    public bool ShouldPass { get; init; } = ShouldPass;
}

[EdictStream("LifecycleProbe")]
public sealed partial record LifecycleProbeRaisedEvent(Guid Key, int Value) : EdictEvent
{
    [EdictRouteKey]
    public Guid Key { get; init; } = Key;

    public int Value { get; init; } = Value;
}

public sealed class LifecycleValidatedCommandValidator : AbstractValidator<LifecycleValidatedCommand>
{
    public LifecycleValidatedCommandValidator()
    {
        RuleFor(command => command.ShouldPass)
            .Equal(true)
            .WithErrorCode("probe_validation_failed")
            .WithMessage("Validation was instructed to fail.");
    }
}

// Hand-written probe interface (Orleans codegen sees this, unlike the
// Edict-generated grain interface) so a test can drive the lifecycle and read
// back its observable effects.
public interface ILifecycleProbe : IGrainWithGuidKey
{
    Task<EdictCommandResult> AcceptedMutateAsync(int delta);
    Task<EdictCommandResult> RejectedMutateAsync(int delta);
    Task<EdictCommandResult> AcceptedRaiseAsync(int delta, int eventValue);
    Task<EdictCommandResult> RaiseThenRejectAsync(int delta, int eventValue);
    Task ThrowAfterMutateAndRaiseAsync(int delta, int eventValue);
    Task<EdictCommandResult> ValidatedMutateAsync(int delta, bool shouldPass);
    Task<int> GetCounterAsync();
    Task<Guid> GetActivationIdAsync();
    Task DeactivateAsync();
}

// Drives ValidateAndHandleAsync directly from hand-written methods, so the
// command lifecycle under test is the Edict.Core method, not a generated
// dispatch spine. The grain declares no HandleAsync, so the generator emits no
// spine and the hand-written DispatchAsync below stands alone.
public partial class LifecycleProbe : EdictCommandHandler<LifecycleProbeState>, ILifecycleProbe
{
    Guid _activationId;

    public override Task<EdictCommandResult> DispatchAsync(EdictCommand command) =>
        throw new NotSupportedException("LifecycleProbe drives ValidateAndHandleAsync directly.");

    // A fresh id per activation lets a test poll until reactivation, the
    // reliable substitute for DeactivateOnIdle + a fixed delay.
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationId = Guid.NewGuid();
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<Guid> GetActivationIdAsync() => Task.FromResult(_activationId);

    public Task<EdictCommandResult> AcceptedMutateAsync(int delta) =>
        ValidateAndHandleAsync(new LifecycleProbeCommand(this.GetPrimaryKey()), () =>
        {
            State.Counter += delta;
            return Accepted();
        });

    public Task<EdictCommandResult> RejectedMutateAsync(int delta) =>
        ValidateAndHandleAsync(new LifecycleProbeCommand(this.GetPrimaryKey()), () =>
        {
            State.Counter += delta;
            return Rejected();
        });

    public Task<EdictCommandResult> AcceptedRaiseAsync(int delta, int eventValue) =>
        ValidateAndHandleAsync(new LifecycleProbeCommand(this.GetPrimaryKey()), () =>
        {
            State.Counter += delta;
            Raise(new LifecycleProbeRaisedEvent(this.GetPrimaryKey(), eventValue));
            return Accepted();
        });

    public Task<EdictCommandResult> RaiseThenRejectAsync(int delta, int eventValue) =>
        ValidateAndHandleAsync(new LifecycleProbeCommand(this.GetPrimaryKey()), () =>
        {
            State.Counter += delta;
            Raise(new LifecycleProbeRaisedEvent(this.GetPrimaryKey(), eventValue));
            return Rejected();
        });

    public Task ThrowAfterMutateAndRaiseAsync(int delta, int eventValue) =>
        ValidateAndHandleAsync(new LifecycleProbeCommand(this.GetPrimaryKey()), () =>
        {
            State.Counter += delta;
            Raise(new LifecycleProbeRaisedEvent(this.GetPrimaryKey(), eventValue));
            throw new InvalidOperationException("probe handler threw after mutating State.");
        });

    public Task<EdictCommandResult> ValidatedMutateAsync(int delta, bool shouldPass) =>
        ValidateAndHandleAsync(new LifecycleValidatedCommand(this.GetPrimaryKey(), shouldPass), () =>
        {
            State.Counter += delta;
            return Accepted();
        });

    public Task<int> GetCounterAsync() => Task.FromResult(State.Counter);

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    static Task<EdictCommandResult> Accepted() =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

    static Task<EdictCommandResult> Rejected() =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
            [new EdictRejectionReason("probe_rejected", "Rejected after the handler completed.")]));
}

public interface ILifecycleEventCaptureGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<int>> GetCapturedValuesAsync();
}

// Subscribes to the probe's domain stream and buffers the raised values so a
// test can assert which events the inline drain published.
[ImplicitStreamSubscription("LifecycleProbe")]
public sealed class LifecycleEventCaptureGrain : Grain, ILifecycleEventCaptureGrain
{
    readonly List<int> _values = [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("LifecycleProbe", this.GetPrimaryKey()));
        await stream.SubscribeAsync(
            (item, _) =>
            {
                if (item is LifecycleProbeRaisedEvent raised)
                {
                    _values.Add(raised.Value);
                }
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<int>> GetCapturedValuesAsync() =>
        Task.FromResult<IReadOnlyList<int>>(_values.AsReadOnly());
}
