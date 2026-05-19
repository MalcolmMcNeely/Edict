using Edict.Contracts.Commands;

using MessagePack;

namespace Edict.Core.Tests.Saga;

/// <summary>Command a test saga dispatches; routed to <c>SagaTrackerCommandHandler</c>.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record SagaTrackerCommand(Guid WorkflowId) : EdictCommand
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}
