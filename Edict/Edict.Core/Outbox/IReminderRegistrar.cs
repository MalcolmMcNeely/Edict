namespace Edict.Core.Outbox;

interface IReminderRegistrar
{
    Task RegisterOrUpdateReminderAsync(string name, TimeSpan dueTime, TimeSpan period);
    Task UnregisterReminderAsync(string name);
}
