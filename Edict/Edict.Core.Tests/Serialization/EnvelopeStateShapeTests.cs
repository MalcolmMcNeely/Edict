using Edict.Contracts;
using Edict.Core.Idempotency;
using Edict.Core.Outbox;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Serialization;

// Drift guard for the persisted grain-state envelope (ADR 0017 frozen-alias
// rule). The envelope is Orleans [GenerateSerializer] grain state, so the guard
// round-trips it through the *Orleans* serializer (binary on Azure, the actual
// persistence path) rather than MessagePack: a renamed/removed [Id] member or a
// changed [Alias] drops the value on the round-trip and fails CI on the
// snapshot diff. Inputs are fixed constants so the literals are the assertion.

public sealed class EnvelopeStateShapeTests
{
    private static readonly Guid EntryId = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid RingId = new("cccccccc-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    private static Serializer BuildSerializer()
    {
        var serviceProvider = new ServiceCollection()
            .AddSerializer(builder => builder
                .AddAssembly(typeof(GrainEnvelope<>).Assembly)
                .AddEdictContractSerializer())
            .BuildServiceProvider();

        return serviceProvider.GetRequiredService<Serializer>();
    }

    private static OutboxSlice PopulatedSlice() =>
        new OutboxSlice()
            .Enqueue(new OutboxEntry
            {
                EntryId = EntryId,
                Kind = OutboxEffectKind.UpsertRow,
                Payload = [9, 8, 7],
                TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
                TraceState = "edict=1",
                AttemptCount = 2,
                NextAttemptUtc = Now,
            })
            .Enqueue(new OutboxEntry { EntryId = EntryId, Kind = OutboxEffectKind.PublishEvent })
            .DeadLetterHead(Now, "max attempts exhausted");

    [Fact]
    public Task GrainEnvelope_ShouldRoundTripPersistedShape_ForStatelessUnitPayload()
    {
        var serializer = BuildSerializer();
        var envelope = new GrainEnvelope<EdictUnit> { Payload = default, Outbox = PopulatedSlice() };

        var bytes = serializer.SerializeToArray(envelope);
        var roundTripped = serializer.Deserialize<GrainEnvelope<EdictUnit>>(bytes);

        return Verify(roundTripped).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task GrainEnvelope_ShouldRoundTripPersistedShape_ForIdempotencyPayload()
    {
        var serializer = BuildSerializer();
        var payload = new IdempotencyPayload<EdictUnit>();
        payload.Ring.Ring = [RingId, Guid.Empty, Guid.Empty];
        payload.Ring.Head = 1;
        payload.Ring.Count = 1;

        var envelope = new GrainEnvelope<IdempotencyPayload<EdictUnit>>
        {
            Payload = payload,
            Outbox = PopulatedSlice(),
        };

        var bytes = serializer.SerializeToArray(envelope);
        var roundTripped = serializer.Deserialize<GrainEnvelope<IdempotencyPayload<EdictUnit>>>(bytes);

        return Verify(roundTripped).DontScrubGuids().DontScrubDateTimes();
    }
}
