using System.Collections.Generic;
using System.Diagnostics;

using Edict.Contracts.Audit;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Orleans.Runtime;
using Orleans.Serialization;

namespace Edict.Core.Audit;

// The audit drain engine a Command Handler composes, parallel to OutboxHost but
// on the audit lifecycle: append-only, infinite retention, and a drain failure
// is a compliance signal rather than an effect retry. Records are staged into the
// AuditChain slice synchronously with the action's grain-state write; this drains
// them to the WORM store off that hot path. Idempotent: a crash between the store
// append and the ack re-appends (the store dedups on record id) then acks.
sealed class AuditHost<TPayload>
    where TPayload : new()
{
    internal const string DrainReminderName = "edict-audit-drain";

    readonly IPersistentState<GrainEnvelope<TPayload>> _state;
    readonly IReminderRegistrar _reminders;
    readonly IEdictAuditStore _store;
    readonly Serializer _serializer;
    readonly TimeSpan _reminderPeriod;
    readonly string _grainTypeName;

    bool _drainReminderRegistered;

    public AuditHost(
        IPersistentState<GrainEnvelope<TPayload>> state,
        IReminderRegistrar reminders,
        IEdictAuditStore store,
        Serializer serializer,
        TimeSpan reminderPeriod,
        string grainTypeName)
    {
        _state = state;
        _reminders = reminders;
        _store = store;
        _serializer = serializer;
        _reminderPeriod = reminderPeriod;
        _grainTypeName = grainTypeName;
    }

    GrainEnvelope<TPayload> State => _state.State;

    /// <summary>Drain-on-activation: writes anything a crash left staged before the grain serves traffic.</summary>
    public async Task OnActivateAsync()
    {
        if (State.Audit is { Pending.Count: > 0 })
        {
            await DrainAsync();
        }
    }

    /// <summary>Reminder tick — the durable retry path after a drain failure.</summary>
    public Task ReceiveReminderAsync()
    {
        _drainReminderRegistered = true;
        return DrainAsync();
    }

    /// <summary>
    /// Writes the staged records to the WORM store, then acks them in one
    /// state write. A store failure keeps the records staged (durable in grain
    /// state), counts the compliance signal, and arms the reminder to retry; it
    /// never drops a record.
    /// </summary>
    public async Task DrainAsync()
    {
        if (State.Audit is not { Pending.Count: > 0 } chain)
        {
            await UnregisterDrainReminderAsync();
            return;
        }

        using var activity = EdictDiagnostics.ActivitySource.StartEdictAuditDrain(_grainTypeName);

        var pending = chain.Pending;
        var records = new EdictAuditRecord[pending.Count];
        for (var i = 0; i < pending.Count; i++)
        {
            records[i] = _serializer.Deserialize<EdictAuditRecord>(pending[i].Record);
        }

        try
        {
            await _store.AppendAsync(records, CancellationToken.None);
        }
        catch (Exception exception)
        {
            AuditMetrics.DrainFailure.Add(1,
                new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, _grainTypeName));
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            await RegisterDrainReminderAsync();
            return;
        }

        foreach (var entry in pending)
        {
            State.Audit = State.Audit!.Ack(entry.RecordId);
        }

        await _state.WriteStateAsync();
        await UnregisterDrainReminderAsync();
    }

    async Task RegisterDrainReminderAsync()
    {
        await _reminders.RegisterOrUpdateReminderAsync(DrainReminderName, _reminderPeriod, _reminderPeriod);
        _drainReminderRegistered = true;
    }

    async Task UnregisterDrainReminderAsync()
    {
        if (!_drainReminderRegistered)
        {
            return;
        }

        await _reminders.UnregisterReminderAsync(DrainReminderName);
        _drainReminderRegistered = false;
    }
}
