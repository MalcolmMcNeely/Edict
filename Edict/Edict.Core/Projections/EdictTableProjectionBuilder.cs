using System.Diagnostics;
using System.Text.Json;

using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.TableStorage;
using Edict.Core.Outbox;
using Edict.Core.TableStorage;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Projections;

/// <summary>
/// Projection builder whose read model lives in an external keyed store so grain
/// activation stays small regardless of how large the model grows (ADR 0012 / ADR 0015).
/// The backing store is supplied via <see cref="IEdictTableStoreFactory"/>; Azure is
/// one implementation — a future DynamoDB or in-memory provider implements the same seam.
/// <para>
/// The row write is expressed as an <see cref="OutboxEffectKind.UpsertRow"/>
/// effect committed atomically with the dedup-ring commit in the one
/// grain-state write, then drained at-least-once (ADR 0018). The upsert is
/// idempotent by pk/rk (a full-row replace), so at-least-once redelivery of the
/// effect does not double-apply. This <b>closes</b> ADR 0012's former
/// double-apply gap — it is no longer an accepted limitation.
/// </para>
/// </summary>
public abstract class EdictTableProjectionBuilder<T>(IEdictTableStoreFactory writeStoreFactory) : EdictProjectionBuilder
    where T : class, IEdictPersistedState, new()
{
    IEdictTableWriteStore<T>? _writeStore;
    OutboxEntry? _pendingUpsert;

    /// <summary>Provider-specific table or collection name for this projection.</summary>
    protected abstract string TableName { get; }

    /// <summary>
    /// Derives the RowKey from the incoming event. The PartitionKey defaults to
    /// <see cref="DefaultPartitionKey"/> (the grain's primary key, which equals the
    /// event's <c>[EdictRouteKey]</c> value for per-aggregate projections).
    /// </summary>
    protected abstract string GetRowKey(EdictEvent evt);

    /// <summary>
    /// The grain's primary key as a string. For per-aggregate projections this equals
    /// the event's <c>[EdictRouteKey]</c> Guid, making it the natural PartitionKey.
    /// Global-singleton projections override to use a different strategy (e.g. the
    /// built-in dead-letter projection collapses every entry into one fixed
    /// partition for cheap fleet-wide reads, ADR 0022).
    /// </summary>
    protected virtual string DefaultPartitionKey => this.GetPrimaryKey().ToString();

    /// <summary>
    /// The row loaded (or freshly constructed) before each handler invocation.
    /// Modifications the handler makes to this instance are captured into the
    /// <see cref="OutboxEffectKind.UpsertRow"/> effect after the handler returns.
    /// The setter is <c>protected</c> so a handler can replace the row wholesale
    /// when the row type is immutable (e.g. a record with <c>init</c>-only
    /// properties).
    /// </summary>
    protected T CurrentRow { get; set; } = new();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        _writeStore = await writeStoreFactory.CreateAsync<T>(TableName, cancellationToken);
    }

    /// <summary>
    /// Wraps every handler call with load-apply-stage. The base
    /// <see cref="EdictProjectionBuilder.DispatchEventAsync{TEvent}"/> default is a
    /// direct handler call; this override loads the row, runs the handler, then
    /// stages the computed row as an <see cref="OutboxEffectKind.UpsertRow"/>
    /// effect (the actual store write happens in the engine drain, atomic with
    /// the dedup-ring commit — ADR 0018).
    /// </summary>
    protected override async Task DispatchEventAsync<TEvent>(TEvent evt, Func<TEvent, Task> handler)
    {
        var partitionKey = DefaultPartitionKey;
        var rowKey = GetRowKey(evt);

        CurrentRow = await _writeStore!.GetAsync(partitionKey, rowKey) ?? new T();

        await handler(evt);

        _pendingUpsert = BuildUpsertEntry(partitionKey, rowKey, CurrentRow);
    }

    /// <inheritdoc />
    protected override IReadOnlyList<OutboxEntry> CollectPendingOutboxEntries()
    {
        if (_pendingUpsert is null)
        {
            return [];
        }

        var entry = _pendingUpsert;
        _pendingUpsert = null;
        return [entry];
    }

    OutboxEntry BuildUpsertEntry(string partitionKey, string rowKey, T row)
    {
        // ADR 0027: the row type identity that travels with the effect is the
        // frozen [Alias] literal, captured here via TypeConverter.Format so the
        // string that survives a class rename is what the drain resolves with
        // TypeConverter.Parse. Replaces the previous AssemblyQualifiedName hop
        // that dead-lettered on rename or move.
        var typeConverter = ServiceProvider.GetRequiredService<Orleans.Serialization.TypeSystem.TypeConverter>();
        var effect = new UpsertRowEffect
        {
            TableName = TableName,
            PartitionKey = partitionKey,
            RowKey = rowKey,
            RowAlias = typeConverter.Format(typeof(T)),
            RowJson = JsonSerializer.SerializeToUtf8Bytes(row),
        };

        // Nest the deferred upsert under the live handle span as parent-child,
        // even when a crash-recovery drain runs much later (ADR 0003).
        var current = Activity.Current;
        var traceParent = current is not null
            ? ActivityExtensions.BuildTraceParent(current.TraceId.ToHexString(), current.SpanId.ToHexString())
            : null;

        var serializer = ServiceProvider.GetRequiredService<Serializer>();

        return new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.UpsertRow,
            Payload = serializer.SerializeToArray(effect),
            TraceParent = traceParent,
            TraceState = current?.TraceStateString,
        };
    }
}
