using System.Diagnostics.Metrics;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Sagas;
using Edict.Telemetry;

using Xunit;

namespace Edict.Testing.Tests;

/// <summary>
/// ADR-0040 Slice-2 acceptance: <c>edict.saga.progress.age</c> grows as the
/// <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider"/> advances
/// without a new saga event. The harness's <c>AdvanceClock</c> moves the
/// silo's clock; <see cref="EdictTestApp.GetSagaState"/> reads the cache the
/// saga's <c>DispatchEventAsync</c> pushed to at handle-completion, so the
/// difference between <c>now</c> and the cached timestamp is the value the
/// observable gauge would emit at scrape time.
/// </summary>
public sealed class SagaProgressAgeProbeTests
{
    [Fact]
    public async Task SagaProgressAge_ShouldGrow_AsClockAdvancesWithoutSagaEvent()
    {
        var stickerId = Guid.NewGuid();
        var sagaTypeName = typeof(StickerSaga).FullName!;

        var captures = new List<double>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName
                    && inst.Name == SemanticConventions.Sagas.Meters.ProgressAge)
                {
                    l.EnableMeasurementEvents(inst);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
        {
            foreach (var t in tags)
            {
                if (t.Key == SemanticConventions.Common.Tags.GrainType
                    && (t.Value as string) == sagaTypeName)
                {
                    lock (captures) { captures.Add(value); }
                    return;
                }
            }
        });
        listener.Start();

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(SagaProgressAgeProbeTests).Assembly));

        await app.Send(new IssueStickerCommand(stickerId));
        await app.Drain();

        // Immediately after the saga handled the event the age should be ~0.
        listener.RecordObservableInstruments();
        double initialAge;
        lock (captures)
        {
            Assert.NotEmpty(captures);
            initialAge = captures[^1];
        }

        await app.AdvanceClock(TimeSpan.FromSeconds(30));

        listener.RecordObservableInstruments();
        double agedValue;
        lock (captures)
        {
            agedValue = captures[^1];
        }

        Assert.True(agedValue >= initialAge + 30,
            $"expected progress age to grow by at least 30s after advance; initial={initialAge}, aged={agedValue}");
    }
}

public sealed partial record IssueStickerCommand(Guid StickerId) : EdictCommand
{
    [EdictRouteKey]
    public Guid StickerId { get; init; } = StickerId;
}

[EdictStream("Stickers")]
public sealed partial record StickerIssuedEvent(Guid StickerId) : EdictEvent
{
    [EdictRouteKey]
    public Guid StickerId { get; init; } = StickerId;
}

public sealed partial record StickerAcknowledgedCommand(Guid StickerId) : EdictCommand
{
    [EdictRouteKey]
    public Guid StickerId { get; init; } = StickerId;
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.StickerState")]
public sealed class StickerState : IEdictPersistedState
{
    [Id(0)]
    public int Issued { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Testing.Tests.StickerProgress")]
public sealed class StickerProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

public partial class StickerAggregate : EdictCommandHandler<StickerState>
{
    public Task<EdictCommandResult> Handle(IssueStickerCommand command)
    {
        State.Issued++;
        Raise(new StickerIssuedEvent(command.StickerId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(StickerAcknowledgedCommand command) =>
        Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
}

public partial class StickerSaga : EdictSaga<StickerProgress>
{
    public Task Handle(StickerIssuedEvent evt)
    {
        Progress.Handled++;
        Dispatch(new StickerAcknowledgedCommand(evt.StickerId));
        return Task.CompletedTask;
    }
}
