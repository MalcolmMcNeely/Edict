using Edict.Telemetry;

using Orleans.Runtime;

namespace Edict.Core.Outbox;

sealed class GrainReminderRegistrar : IReminderRegistrar
{
    // Near-cosmetic flat pause between attempts. The per-attempt block on Orleans' own
    // service-start wait dominates wall-clock time, so this stays an internal constant
    // rather than a knob, and the policy is a flat attempt count rather than exponential.
    static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(200);

    readonly Func<string, TimeSpan, TimeSpan, Task> _register;
    readonly Func<string, Task<IGrainReminder?>> _getReminder;
    readonly Func<IGrainReminder, Task> _unregister;
    readonly int _retryCount;
    readonly TimeSpan _retryDelay;

    public GrainReminderRegistrar(Grain grain, int retryCount)
        : this(
            register: (name, dueTime, period) => grain.RegisterOrUpdateReminder(name, dueTime, period),
            getReminder: name => grain.GetReminder(name),
            unregister: reminder => grain.UnregisterReminder(reminder),
            retryCount: retryCount,
            retryDelay: DefaultRetryDelay)
    {
    }

    internal GrainReminderRegistrar(
        Func<string, TimeSpan, TimeSpan, Task> register,
        Func<string, Task<IGrainReminder?>> getReminder,
        Func<IGrainReminder, Task> unregister,
        int retryCount,
        TimeSpan retryDelay)
    {
        _register = register;
        _getReminder = getReminder;
        _unregister = unregister;
        _retryCount = retryCount;
        _retryDelay = retryDelay;
    }

    public Task RegisterOrUpdateReminderAsync(string name, TimeSpan dueTime, TimeSpan period) =>
        ExecuteWithColdStartRetryAsync(
            SemanticConventions.Reminders.Tags.OperationValues.Register,
            () => _register(name, dueTime, period));

    public Task UnregisterReminderAsync(string name) =>
        ExecuteWithColdStartRetryAsync(
            SemanticConventions.Reminders.Tags.OperationValues.Unregister,
            async () =>
            {
                var reminder = await _getReminder(name);
                if (reminder is not null)
                {
                    await _unregister(reminder);
                }
            });

    async Task ExecuteWithColdStartRetryAsync(string operation, Func<Task> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                if (attempt > 1)
                {
                    ReminderRegistrationMetrics.RecordRecovered(operation);
                }
                return;
            }
            catch (OrleansException exception) when (IsReminderServiceInitializing(exception))
            {
                if (attempt >= _retryCount)
                {
                    ReminderRegistrationMetrics.RecordExhausted(operation);
                    throw new EdictReminderRegistrationException(operation, exception);
                }
                // Wall-clock delay, never the injected TimeProvider: scheduled grains and test
                // fixtures run on a frozen FakeTimeProvider, so a clock-driven delay would never
                // elapse and this loop would hang. The retry bridges Orleans' reminder-service
                // cold-start, which is wall-clock reality, not domain time.
                await Task.Delay(_retryDelay);
            }
        }
    }

    // Narrow ordinal match — Orleans exposes no typed exception for this condition. Fails safe:
    // any other message (or a future Orleans reword) is not caught, so it propagates loud on the
    // first attempt with no delay rather than silently swallowing an unrelated reminder fault.
    static bool IsReminderServiceInitializing(OrleansException exception) =>
        exception.Message.Contains("Reminder Service is still initializing", StringComparison.Ordinal);
}
