using Orleans.Runtime;

namespace Edict.Core.Outbox;

/// <summary>
/// Closes over a hosting <see cref="IGrainBase"/> + its <see cref="IRemindable"/>
/// surface so the bare <see cref="OutboxHost{TPayload}"/> can register the lazy
/// drain reminder without holding a grain reference. The one residual
/// indirection composition introduces — Orleans's reminder API is
/// grain-instance-bound. Bare-named.
/// </summary>
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
