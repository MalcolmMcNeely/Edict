namespace Edict.Core.Outbox;

/// <summary>
/// The one residual indirection the composition refactor introduces: Orleans's
/// reminder API is grain-instance-bound, so <see cref="OutboxHost{TPayload}"/>
/// cannot register reminders without a back-reference to the hosting grain.
/// Each grain shell constructs a closure over <c>this</c> and hands it to the
/// host, so the host stays a plain class that's trivially fakeable in tests.
/// Bare-named — no consumer types it.
/// </summary>
interface IReminderRegistrar
{
    Task RegisterOrUpdateReminderAsync(string name, TimeSpan dueTime, TimeSpan period);
    Task UnregisterReminderAsync(string name);
}
