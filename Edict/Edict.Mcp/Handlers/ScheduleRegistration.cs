namespace Edict.Mcp.Handlers;

enum ScheduleTimeoutSource
{
    InheritsSiloDefault,
    InheritsSagaCap,
}

sealed record ScheduleRegistration(ScheduleTimeoutSource TimeoutSource)
{
    public static readonly ScheduleRegistration InheritsSiloDefault = new(ScheduleTimeoutSource.InheritsSiloDefault);
    public static readonly ScheduleRegistration InheritsSagaCap = new(ScheduleTimeoutSource.InheritsSagaCap);
}
