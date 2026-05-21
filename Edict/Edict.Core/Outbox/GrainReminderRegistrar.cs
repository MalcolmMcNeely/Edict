using Orleans.Runtime;

namespace Edict.Core.Outbox;

sealed class GrainReminderRegistrar(Grain grain) : IReminderRegistrar
{
    public async Task RegisterOrUpdateReminderAsync(string name, TimeSpan dueTime, TimeSpan period) =>
        await grain.RegisterOrUpdateReminder(name, dueTime, period);

    public async Task UnregisterReminderAsync(string name)
    {
        var reminder = await grain.GetReminder(name);
        if (reminder is not null)
        {
            await grain.UnregisterReminder(reminder);
        }
    }
}
