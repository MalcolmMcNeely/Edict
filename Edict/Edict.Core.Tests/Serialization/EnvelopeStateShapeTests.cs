using Edict.Contracts;
using Edict.Core.Idempotency;
using Edict.Core.Outbox;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Serialization;

// Round-trips through the Orleans serializer rather than MessagePack: the
// envelope is Orleans [GenerateSerializer] grain state, and binary on Azure
// is the actual persistence path.
public sealed class EnvelopeStateShapeTests
{
    static readonly Guid EntryId = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid HandledEventId = new("cccccccc-0000-0000-0000-000000000003");
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    static Serializer BuildSerializer()
    {
        var serviceProvider = new ServiceCollection()
            .AddSerializer(builder => builder
                .AddAssembly(typeof(GrainEnvelope<>).Assembly)
                .AddEdictContractSerializer())
            .BuildServiceProvider();

        return serviceProvider.GetRequiredService<Serializer>();
    }

    static OutboxSlice PopulatedSlice() =>
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
            .Enqueue(new OutboxEntry { EntryId = EntryId, Kind = OutboxEffectKind.PublishEvent });

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
    public Task GrainEnvelope_ShouldRoundTripPersistedShape_ForPopulatedIdempotency()
    {
        var serializer = BuildSerializer();
        var envelope = new GrainEnvelope<EdictUnit>
        {
            Payload = default,
            Outbox = PopulatedSlice(),
            Idempotency = new IdempotencyState
            {
                HandledEventIds = [HandledEventId, Guid.Empty, Guid.Empty],
                Head = 1,
                Count = 1,
            },
        };

        var bytes = serializer.SerializeToArray(envelope);
        var roundTripped = serializer.Deserialize<GrainEnvelope<EdictUnit>>(bytes);

        return Verify(roundTripped).DontScrubGuids().DontScrubDateTimes();
    }
}
