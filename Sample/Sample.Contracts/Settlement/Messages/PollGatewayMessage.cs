using Edict.Contracts.Schedules;

namespace Sample.Contracts.Settlement.Messages;

/// <summary>
/// The schedule message the settlement saga fires on each poll cadence. Carries
/// no data: the poll count and payment id live in durable saga Progress, read
/// fresh each fire.
/// </summary>
public sealed partial record PollGatewayMessage : EdictScheduleMessage;
