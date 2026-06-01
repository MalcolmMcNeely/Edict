using Edict.Contracts;
using Edict.Core.Idempotency;
using Edict.Core.Outbox;

using Orleans.Runtime;

namespace Edict.Core.Tests.TestSupport;

// Grain-state double whose WriteStateAsync throws on the Nth write (1-based via
// FailOnWrite) and otherwise snapshots State into a Durable clone, so a test can
// assert what actually reached durable storage versus the live in-memory view.
sealed class FaultInjectingPersistentState : IPersistentState<GrainEnvelope<EdictUnit>>
{
    int _writes;

    public int FailOnWrite { get; init; }
    public GrainEnvelope<EdictUnit> State { get; set; } = new();
    public GrainEnvelope<EdictUnit> Durable { get; private set; } = new();

    public string Etag => string.Empty;
    public bool RecordExists => true;

    public Task WriteStateAsync()
    {
        _writes++;
        if (_writes == FailOnWrite)
        {
            throw new InvalidOperationException("simulated write fault");
        }

        Durable = Clone(State);
        return Task.CompletedTask;
    }

    public Task ReadStateAsync() => Task.CompletedTask;
    public Task ClearStateAsync() => Task.CompletedTask;

    static GrainEnvelope<EdictUnit> Clone(GrainEnvelope<EdictUnit> source) => new()
    {
        Payload = source.Payload,
        Outbox = source.Outbox,
        Idempotency = new IdempotencyState
        {
            HandledEventIds = (Guid[])source.Idempotency.HandledEventIds.Clone(),
            Head = source.Idempotency.Head,
            Count = source.Idempotency.Count,
        },
    };
}
