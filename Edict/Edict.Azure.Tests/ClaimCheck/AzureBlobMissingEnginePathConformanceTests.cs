using Azure.Storage.Blobs;

using Edict.Azure.ClaimCheck;
using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.EventHandler;
using Edict.Core.Outbox;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Azure.Tests.ClaimCheck;

/// <summary>
/// Azurite-backed conformance for the receiver-side missing-blob dead-letter
/// path after the fold: a pointer-bearing
/// <see cref="EdictEventEnvelope"/> staged as an
/// <see cref="OutboxEffectKind.InvokeHandler"/> entry is drained by the
/// engine; <see cref="InvokeHandlerExecutor"/> calls
/// <see cref="ClaimCheckUnwrap"/> which fetches from the real
/// <see cref="AzureBlobClaimCheckStore"/>, surfaces
/// <see cref="Azure.RequestFailedException"/> (status 404), the engine's
/// per-entry retry catches the throw, bumps backoff, and on
/// <see cref="EdictOutboxOptions.MaxAttempts"/> exhaustion routes
/// <see cref="DeadLetterPromoter.Promote"/> through the BlobMissing branch.
/// Replaces the prior <c>HandleBlobMissingAsync</c> integration coverage
/// against the Azure provider stack — the
/// <c>BlobMissingDeadLetterEndToEndTests</c> in <c>Edict.Core.Tests</c> proves
/// the in-memory variant; this test proves the Azurite-backed exception shape
/// flows through the same engine path unchanged.
/// </summary>
public sealed class AzureBlobMissingEnginePathConformanceTests : IAsyncLifetime
{
    BlobServiceClient _blobServiceClient = null!;
    Serializer _serializer = null!;
    string _claimCheckContainerName = "";

    public async Task InitializeAsync()
    {
        var connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _blobServiceClient = new BlobServiceClient(connectionString);
        _claimCheckContainerName = $"edict-claim-check-conformance-{Guid.NewGuid():N}";

        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(AzureBlobMissingEnginePathConformanceTests).Assembly);
            b.AddEdictContractSerializer();
        });
        _serializer = services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EnginePath_ShouldPromoteToBlobMissingDeadLetter_WhenAzureBlobClaimCheckStoreReturns404()
    {
        var store = await AzureBlobClaimCheckStore.CreateAsync(_blobServiceClient, _claimCheckContainerName);
        var unwrap = new ClaimCheckUnwrap(_serializer, store);
        var invokeExecutor = new InvokeHandlerExecutor(_serializer, unwrap);
        var publishExecutor = new RecordingPublishEventExecutor(_serializer);

        var missingKey = $"edict-claim-check/{Guid.NewGuid():N}";
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: missingKey)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            InnerEventStreamName = "AzureBlobMissingConformance",
            InnerEventRouteKey = Guid.NewGuid(),
        };
        var entry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = _serializer.SerializeToArray<EdictEvent>(envelope),
        };

        // Real clock is fine — MaxAttempts=1 promotes on the first failure so
        // backoff arithmetic never gates the second attempt.
        var clock = TimeProvider.System;
        var state = new FakePersistentState
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(entry),
            },
        };
        var reminders = new FakeReminderRegistrar();
        var promoter = new DeadLetterPromoter(_serializer, new ServiceCollection().BuildServiceProvider());

        var host = new OutboxHost<EdictUnit>(
            state,
            new FakeStreamProvider(),
            reminders,
            [invokeExecutor, publishExecutor],
            new EdictOptions
            {
                OutboxMaxAttempts = 1,
                OutboxBaseDelay = TimeSpan.FromMilliseconds(10),
                OutboxJitterFraction = 0,
            },
            clock,
            promoter,
            grainKey: "11111111-1111-1111-1111-111111111111",
            grainTypeName: "Sample.OrderEmailHandler",
            deferredDispatch: _ => Task.CompletedTask,
            consumerType: typeof(AzureBlobMissingEnginePathConformanceTests));

        await host.DrainAsync();

        // After exhaustion the failing InvokeHandler entry is gone; the
        // promoted EdictDeadLetterRaised PublishEvent succeeded against the
        // recording executor and was acked, leaving Pending empty.
        Assert.Empty(state.State.Outbox.Pending);

        // The recording executor captured the serialised dead-letter event so
        // the BlobMissing failure-kind discriminator and the original
        // claim-check key are observable on the row that would land in the
        // forensic projection.
        var raised = Assert.Single(publishExecutor.Published);
        Assert.Equal(EdictDeadLetterFailureKind.BlobMissing, raised.FailureKind);
        Assert.Equal(missingKey, raised.ClaimCheckKey);
        Assert.Equal("Azure.RequestFailedException", raised.ExceptionType);
        Assert.Equal("Sample.OrderEmailHandler", raised.SourceGrainType);
    }

    sealed class FakePersistentState : IPersistentState<GrainEnvelope<EdictUnit>>
    {
        public GrainEnvelope<EdictUnit> State { get; set; } = new();
        public string Etag => "";
        public bool RecordExists => true;
        public Task WriteStateAsync() => Task.CompletedTask;
        public Task ReadStateAsync() => Task.CompletedTask;
        public Task ClearStateAsync() => Task.CompletedTask;
    }

    sealed class FakeReminderRegistrar : IReminderRegistrar
    {
        public Task RegisterOrUpdateReminderAsync(string name, TimeSpan dueTime, TimeSpan period) => Task.CompletedTask;
        public Task UnregisterReminderAsync(string name) => Task.CompletedTask;
    }

    sealed class FakeStreamProvider : IStreamProvider
    {
        public string Name => "edict";
        public bool IsRewindable => false;
        public IAsyncStream<T> GetStream<T>(StreamId streamId) =>
            throw new NotSupportedException("FakeStreamProvider has no streams.");
    }

    sealed class RecordingPublishEventExecutor(Serializer serializer) : IOutboxEffectExecutor
    {
        public List<EdictDeadLetterRaised> Published { get; } = [];
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

        public Task ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType)
        {
            var raised = serializer.Deserialize<EdictEvent>(entry.Payload);
            Published.Add(Assert.IsType<EdictDeadLetterRaised>(raised));
            return Task.CompletedTask;
        }
    }
}
