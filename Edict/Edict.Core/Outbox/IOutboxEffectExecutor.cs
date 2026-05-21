using Edict.Contracts.Events;

using Orleans.Streams;

namespace Edict.Core.Outbox;

interface IOutboxEffectExecutor
{
    OutboxEffectKind Kind { get; }

    Task ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task>? deferredDispatch,
        Type? consumerType);
}
