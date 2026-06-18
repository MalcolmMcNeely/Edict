using System.Diagnostics.Metrics;

using Edict.Core.Outbox;
using Edict.Telemetry;

using Orleans.Runtime;

using Xunit;

namespace Edict.Core.Tests.Outbox;

// The cold-start retry is an internal property of the registrar, driven here through the
// internal test constructor's injected operation delegates — no cluster, no real reminder
// service, no wall-clock wait (the delay is injected as zero). The transient signal is a
// fabricated OrleansException whose message contains the "still initializing" marker; any
// other OrleansException is non-transient and must propagate untouched.
public sealed class ReminderRegistrarRetryTests
{
    const int RetryCount = 3;

    static OrleansException ColdStartFault(int ordinal) =>
        new($"Reminder Service is still initializing and it is taking a long time. Please retry again later. (#{ordinal})");

    [Fact]
    public async Task RegisterOrUpdateReminder_RecoversAfterColdStartTransients_CompletesAndRecordsRecoveredOnce()
    {
        // Arrange
        var attempts = 0;
        var captures = new List<Capture>();
        using var listener = StartListener(captures);
        var registrar = new GrainReminderRegistrar(
            register: (_, _, _) =>
            {
                attempts++;
                if (attempts < RetryCount)
                {
                    throw ColdStartFault(attempts);
                }
                return Task.CompletedTask;
            },
            getReminder: _ => Task.FromResult<IGrainReminder?>(null),
            unregister: _ => Task.CompletedTask,
            retryCount: RetryCount,
            retryDelay: TimeSpan.Zero);

        // Act
        await registrar.RegisterOrUpdateReminderAsync("backstop", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        // Assert
        Assert.Equal(RetryCount, attempts);
        var capture = Assert.Single(captures);
        Assert.Equal(1L, capture.Value);
        Assert.Equal(SemanticConventions.Reminders.Tags.OperationValues.Register, capture.Tag(SemanticConventions.Reminders.Tags.Operation));
        Assert.Equal(SemanticConventions.Reminders.Tags.OutcomeValues.Recovered, capture.Tag(SemanticConventions.Reminders.Tags.Outcome));
    }

    [Fact]
    public async Task RegisterOrUpdateReminder_ExhaustsBudget_ThrowsTypedWithLastFaultAsInnerAndRecordsExhausted()
    {
        // Arrange
        var attempts = 0;
        var captures = new List<Capture>();
        using var listener = StartListener(captures);
        var registrar = new GrainReminderRegistrar(
            register: (_, _, _) =>
            {
                attempts++;
                throw ColdStartFault(attempts);
            },
            getReminder: _ => Task.FromResult<IGrainReminder?>(null),
            unregister: _ => Task.CompletedTask,
            retryCount: RetryCount,
            retryDelay: TimeSpan.Zero);

        // Act
        var thrown = await Assert.ThrowsAsync<EdictReminderRegistrationException>(
            () => registrar.RegisterOrUpdateReminderAsync("backstop", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)));

        // Assert
        Assert.Equal(RetryCount, attempts);
        var inner = Assert.IsType<OrleansException>(thrown.InnerException);
        Assert.Contains($"(#{RetryCount})", inner.Message);
        var capture = Assert.Single(captures);
        Assert.Equal(1L, capture.Value);
        Assert.Equal(SemanticConventions.Reminders.Tags.OperationValues.Register, capture.Tag(SemanticConventions.Reminders.Tags.Operation));
        Assert.Equal(SemanticConventions.Reminders.Tags.OutcomeValues.Exhausted, capture.Tag(SemanticConventions.Reminders.Tags.Outcome));
    }

    [Fact]
    public async Task RegisterOrUpdateReminder_NonTransientOrleansException_PropagatesImmediatelyWithoutRetryOrMetric()
    {
        // Arrange
        var attempts = 0;
        var captures = new List<Capture>();
        using var listener = StartListener(captures);
        var registrar = new GrainReminderRegistrar(
            register: (_, _, _) =>
            {
                attempts++;
                throw new OrleansException("Reminder table is unavailable.");
            },
            getReminder: _ => Task.FromResult<IGrainReminder?>(null),
            unregister: _ => Task.CompletedTask,
            retryCount: RetryCount,
            retryDelay: TimeSpan.Zero);

        // Act
        var thrown = await Assert.ThrowsAsync<OrleansException>(
            () => registrar.RegisterOrUpdateReminderAsync("backstop", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)));

        // Assert — a single invocation proves no retry and therefore no delay, and the bare
        // Orleans fault propagates as itself rather than being wrapped.
        Assert.Equal(1, attempts);
        Assert.Equal("Reminder table is unavailable.", thrown.Message);
        Assert.Empty(captures);
    }

    [Fact]
    public async Task UnregisterReminder_RecoversAfterColdStartTransients_RecordsRecoveredTaggedUnregister()
    {
        // Arrange
        var attempts = 0;
        var captures = new List<Capture>();
        using var listener = StartListener(captures);
        var registrar = new GrainReminderRegistrar(
            register: (_, _, _) => Task.CompletedTask,
            getReminder: _ =>
            {
                attempts++;
                if (attempts < RetryCount)
                {
                    throw ColdStartFault(attempts);
                }
                return Task.FromResult<IGrainReminder?>(null);
            },
            unregister: _ => Task.CompletedTask,
            retryCount: RetryCount,
            retryDelay: TimeSpan.Zero);

        // Act
        await registrar.UnregisterReminderAsync("backstop");

        // Assert
        Assert.Equal(RetryCount, attempts);
        var capture = Assert.Single(captures);
        Assert.Equal(1L, capture.Value);
        Assert.Equal(SemanticConventions.Reminders.Tags.OperationValues.Unregister, capture.Tag(SemanticConventions.Reminders.Tags.Operation));
        Assert.Equal(SemanticConventions.Reminders.Tags.OutcomeValues.Recovered, capture.Tag(SemanticConventions.Reminders.Tags.Outcome));
    }

    static MeterListener StartListener(List<Capture> captures)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, enableFor) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.Reminders.Meters.RegistrationRetryCount)
                {
                    enableFor.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var snapshot = new Dictionary<string, object?>(tags.Length);
            foreach (var tag in tags)
            {
                snapshot[tag.Key] = tag.Value;
            }
            captures.Add(new Capture(value, snapshot));
        });
        listener.Start();
        return listener;
    }

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var value) ? value : null;
    }
}
