using System.Diagnostics.Metrics;

using Edict.Contracts;
using Edict.Contracts.Audit;
using Edict.Core.Audit;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using Xunit;

namespace Edict.Core.Tests.Audit;

// Drives AuditHost directly as a deep module: persistent state, reminder registrar,
// chain store, and payload store are all injected, so the drain-failure branch can
// be exercised without a container. Asserts only externally observable behaviour:
// what survives in staged state, what reaches the stores, whether the reminder is
// armed or cleared, and the compliance counter, never private fields or call order.
public sealed class AuditHostTests
{
    static readonly DateTimeOffset Now = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task ArmDrainReminderAsync_ArmsTheDurableReminder()
    {
        // Arrange
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var host = BuildHost(state, log, new FaultInjectingAuditStore(), new FaultInjectingAuditPayloadStore());

        // Act
        await host.ArmDrainReminderAsync();

        // Assert
        Assert.Contains(("reminders", nameof(IReminderRegistrar.RegisterOrUpdateReminderAsync)), log.Entries);
    }

    [Fact]
    public async Task ArmDrainReminderAsync_IsSafeToCallRepeatedly()
    {
        // Arrange
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var host = BuildHost(state, log, new FaultInjectingAuditStore(), new FaultInjectingAuditPayloadStore());

        // Act — register-or-update tolerates repeated arming across stages.
        await host.ArmDrainReminderAsync();
        await host.ArmDrainReminderAsync();

        // Assert
        var registrations = log.Entries.Count(entry =>
            entry == ("reminders", nameof(IReminderRegistrar.RegisterOrUpdateReminderAsync)));
        Assert.Equal(2, registrations);
    }

    [Fact]
    public async Task ArmDrainReminderAsync_ThenSuccessfulDrain_UnregistersReminder()
    {
        // Arrange — a record is staged and the reminder armed at stage time, as the
        // Command Handler does right after the durable grain-state write.
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        StageRecords(state, count: 1);
        var host = BuildHost(state, log, new FaultInjectingAuditStore(), new FaultInjectingAuditPayloadStore());

        // Act
        await host.ArmDrainReminderAsync();
        await host.DrainAsync();

        // Assert — the fast-path drain clears both the staged work and its reminder.
        Assert.Empty(state.State.Audit!.Pending);
        Assert.Contains(("reminders", nameof(IReminderRegistrar.UnregisterReminderAsync)), log.Entries);
    }

    [Fact]
    public async Task DrainAsync_WhenWormWriteFails_LeavesEveryStagedRecordStaged()
    {
        // Arrange
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var chainStore = new FaultInjectingAuditStore();
        var payloadStore = new FaultInjectingAuditPayloadStore { ThrowOnNextPut = true };
        var stagedIds = StageRecords(state, count: 2);
        var host = BuildHost(state, log, chainStore, payloadStore);

        // Act
        await host.DrainAsync();

        // Assert — none acked, none dropped: the staged records are still in state,
        // and neither WORM store took anything.
        Assert.Equal(stagedIds, state.State.Audit!.Pending.Select(entry => entry.RecordId).ToArray());
        Assert.Equal(0, chainStore.Count);
        Assert.Equal(0, payloadStore.Count);
    }

    [Fact]
    public async Task DrainAsync_WhenWormWriteFails_ArmsTheDurableReminder()
    {
        // Arrange
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var payloadStore = new FaultInjectingAuditPayloadStore { ThrowOnNextPut = true };
        StageRecords(state, count: 1);
        var host = BuildHost(state, log, new FaultInjectingAuditStore(), payloadStore);

        // Act
        await host.DrainAsync();

        // Assert
        Assert.Contains(("reminders", nameof(IReminderRegistrar.RegisterOrUpdateReminderAsync)), log.Entries);
    }

    [Fact]
    public async Task DrainAsync_WhenWormWriteFails_IncrementsDrainFailureCounter_TaggedByGrainType()
    {
        // Arrange
        var marker = $"AuditHostTest_{Guid.NewGuid():N}";
        var failures = new List<long>();
        using var listener = StartDrainFailureListener(marker, failures);

        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var payloadStore = new FaultInjectingAuditPayloadStore { ThrowOnNextPut = true };
        StageRecords(state, count: 1);
        var host = BuildHost(state, log, new FaultInjectingAuditStore(), payloadStore, grainTypeName: marker);

        // Act
        await host.DrainAsync();

        // Assert
        Assert.Equal(1L, Assert.Single(failures));
    }

    [Fact]
    public async Task DrainAsync_CleanRetryAfterFailure_FlushesBothStoresAndUnregistersReminder()
    {
        // Arrange
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var chainStore = new FaultInjectingAuditStore();
        var payloadStore = new FaultInjectingAuditPayloadStore { ThrowOnNextPut = true };
        var stagedIds = StageRecords(state, count: 2);
        var host = BuildHost(state, log, chainStore, payloadStore);

        // Act — the first drain faults; the second is clean.
        await host.DrainAsync();
        await host.DrainAsync();

        // Assert — both stores hold every record, nothing is left staged, and the
        // reminder armed by the failure is cleared.
        Assert.Equal(2, chainStore.Count);
        Assert.Equal(2, payloadStore.Count);
        Assert.All(stagedIds, recordId => Assert.True(payloadStore.Contains(recordId)));
        Assert.Empty(state.State.Audit!.Pending);
        Assert.Contains(("reminders", nameof(IReminderRegistrar.UnregisterReminderAsync)), log.Entries);
    }

    [Fact]
    public async Task DrainAsync_WhenChainWriteFailsAfterPayloadSucceeds_RetryRewritesPayloadAndCompletesChain()
    {
        // Arrange — the drain writes bodies first, then the chain; faulting only the
        // chain leaves a body durable with no chain record behind it.
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var chainStore = new FaultInjectingAuditStore { ThrowOnNextAppend = true };
        var payloadStore = new FaultInjectingAuditPayloadStore();
        var stagedIds = StageRecords(state, count: 1);
        var host = BuildHost(state, log, chainStore, payloadStore);

        // Act
        await host.DrainAsync();

        // After the faulted drain the body is durable but the chain is empty and the
        // record is still staged.
        Assert.Equal(0, chainStore.Count);
        Assert.Equal(1, payloadStore.Count);
        Assert.Single(state.State.Audit!.Pending);

        // The retry re-puts the same body (the store dedups on record id) and the
        // chain append now succeeds.
        await host.DrainAsync();

        // Assert
        Assert.Equal(1, chainStore.Count);
        Assert.Equal(1, payloadStore.Count);
        Assert.True(payloadStore.Contains(Assert.Single(stagedIds)));
        Assert.Empty(state.State.Audit!.Pending);
    }

    static Guid[] StageRecords(CountingPersistentState<GrainEnvelope<EdictUnit>> state, int count)
    {
        var chain = new AuditChain();
        var ids = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            var content = new EdictAuditRecord
            {
                RecordId = Guid.NewGuid(),
                Kind = EdictAuditKind.Command,
                Outcome = EdictAuditOutcome.Accepted,
                EntityType = "TestEntity",
                EntityKey = "entity-1",
                MessageType = "TestMessage",
                OccurredAt = Now,
            };
            var sealedRecord = AuditRecordBuilder.Seal(content, chain.Head, chain.NextSequence);
            ids[i] = sealedRecord.RecordId;
            var entry = new AuditChainEntry
            {
                RecordId = sealedRecord.RecordId,
                Record = Serializer.SerializeToArray(sealedRecord),
                Payload = [(byte)(i + 1)],
            };
            chain = chain.Append(sealedRecord.RecordHash, entry);
        }
        state.State.Audit = chain;
        return ids;
    }

    static AuditHost<EdictUnit> BuildHost(
        CountingPersistentState<GrainEnvelope<EdictUnit>> state,
        CallLog log,
        IEdictAuditStore store,
        IEdictAuditPayloadStore payloadStore,
        string grainTypeName = "TestGrain") =>
        new(
            state,
            new RecordingReminderRegistrar(log),
            store,
            payloadStore,
            Serializer,
            ReminderPeriod,
            grainTypeName);

    static MeterListener StartDrainFailureListener(string grainTypeMarker, List<long> failures)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.Audit.Meters.DrainFailure)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name != SemanticConventions.Audit.Meters.DrainFailure)
            {
                return;
            }
            foreach (var tag in tags)
            {
                if (tag.Key == SemanticConventions.Common.Tags.GrainType
                    && (string?)tag.Value == grainTypeMarker)
                {
                    lock (failures)
                    {
                        failures.Add(value);
                    }
                }
            }
        });
        listener.Start();
        return listener;
    }

    static readonly Serializer Serializer = BuildSerializer();

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder =>
        {
            builder.AddAssembly(typeof(AuditHostTests).Assembly);
            builder.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }
}
