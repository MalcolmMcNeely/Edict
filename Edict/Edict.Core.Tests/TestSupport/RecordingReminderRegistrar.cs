using Edict.Core.Outbox;

namespace Edict.Core.Tests.TestSupport;

sealed class RecordingReminderRegistrar(CallLog log, string sourceTag = "reminders") : IReminderRegistrar
{
    public Task RegisterOrUpdateReminderAsync(string name, TimeSpan dueTime, TimeSpan period)
    {
        log.Record(sourceTag, nameof(RegisterOrUpdateReminderAsync));
        return Task.CompletedTask;
    }

    public Task UnregisterReminderAsync(string name)
    {
        log.Record(sourceTag, nameof(UnregisterReminderAsync));
        return Task.CompletedTask;
    }
}
