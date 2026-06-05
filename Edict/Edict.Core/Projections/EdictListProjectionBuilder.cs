using System.Diagnostics;

using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.TableStorage;
using Edict.Core.Idempotency;
using Edict.Core.Outbox;
using Edict.Core.TableStorage;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Serialization.TypeSystem;

namespace Edict.Core.Projections;

/// <summary>
/// Projection builder whose read model lives in an external keyed store so grain
/// activation stays small regardless of how large the model grows.
/// The backing store is supplied via <see cref="IEdictTableStoreFactory"/>; Azure is
/// one implementation — a future DynamoDB or in-memory provider implements the same seam.
/// <para>
/// The row write is expressed as an <see cref="OutboxEffectKind.UpsertRow"/>
/// effect committed atomically with the dedup-ring commit in the one
/// grain-state write, then drained at-least-once. The upsert is
/// idempotent by pk/rk (a full-row replace), so at-least-once redelivery of the
/// effect does not double-apply. This closes the former table-projection
/// double-apply gap — it is no longer an accepted limitation.
/// </para>
/// </summary>
public abstract class EdictListProjectionBuilder<TRow>(IEdictTableStoreFactory writeStoreFactory) : EdictProjectionBuilder
    where TRow : class, IEdictPersistedState, new()
{
    IEdictTableWriteStore<TRow>? _writeStore;
    Serializer? _cachedSerializer;
    TypeConverter? _cachedTypeConverter;
    readonly InvocationScope<ProjectionRowBox<TRow>> _row = new();
    (string PartitionKey, string RowKey, TRow Row)? _lastWrittenRow;

    /// <summary>Provider-specific table or collection name for this projection.</summary>
    protected abstract string TableName { get; }

    /// <summary>
    /// Derives the RowKey from the incoming event. The PartitionKey defaults to
    /// <see cref="DefaultPartitionKey"/> (the grain's primary key, which equals the
    /// event's <c>[EdictRouteKey]</c> value for per-aggregate projections).
    /// </summary>
    protected abstract string GetRowKey(EdictEvent edictEvent);

    /// <summary>
    /// The grain's primary key as a string. For per-aggregate projections this equals
    /// the event's <c>[EdictRouteKey]</c> Guid, making it the natural PartitionKey.
    /// Global-singleton projections override to use a different strategy (e.g. the
    /// built-in dead-letter projection collapses every entry into one fixed
    /// partition for cheap fleet-wide reads).
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
    protected TRow CurrentRow
    {
        get => _row.Current.Row;
        set => _row.Current.Row = value;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        _writeStore = await writeStoreFactory.CreateAsync<TRow>(TableName, cancellationToken);
    }

    /// <summary>
    /// Serves a consumer point-get through the grain. The store partition is
    /// always this grain's <see cref="DefaultPartitionKey"/> — the read facade
    /// addresses the grain by its routing key, so the caller's partition and the
    /// grain key coincide for per-aggregate projections.
    /// </summary>
    public override async Task<object?> EdictReadRowAsync(string rowKey) =>
        await _writeStore!.GetAsync(DefaultPartitionKey, rowKey);

    /// <summary>Serves a consumer partition-query through the grain.</summary>
    public override async Task<IReadOnlyList<object>> EdictReadPartitionAsync()
    {
        var rows = await _writeStore!.QueryPartitionAsync(DefaultPartitionKey);
        return rows.Cast<object>().ToList();
    }

    /// <summary>
    /// Wraps every handler call with load-apply-stage. The base
    /// <see cref="EdictProjectionBuilder.DispatchEventAsync{TEvent}"/> default is a
    /// direct handler call; this override loads the row into a per-invocation
    /// box, runs the handler, then returns the computed row as an
    /// <see cref="OutboxEffectKind.UpsertRow"/> effect (the actual store write
    /// happens in the engine drain, atomic with the dedup-ring commit). The box
    /// is invocation-scoped so a parallel deferred drain cannot cross-wire two
    /// dispatches' working rows.
    /// </summary>
    protected override async Task<EdictDispatchOutcome> DispatchEventAsync<TEvent>(TEvent edictEvent, Func<TEvent, Task> handler)
    {
        var partitionKey = DefaultPartitionKey;
        var rowKey = GetRowKey(edictEvent);

        // Read-your-writes for consecutive events on the same row: the upsert is
        // drained after the handler returns, so reading the store here would
        // miss the just-computed row until that drain lands. Seed from the last
        // row this grain computed for the same (pk, rk); any other key (or first
        // touch) reads the store. The cache is keyed, so a stale entry from a
        // different key falls through to a fresh read rather than misapplying.
        var box = _row.Begin();
        box.Row = _lastWrittenRow is { } cached && cached.PartitionKey == partitionKey && cached.RowKey == rowKey
            ? cached.Row
            : await _writeStore!.GetAsync(partitionKey, rowKey) ?? new TRow();

        await handler(edictEvent);

        _lastWrittenRow = (partitionKey, rowKey, box.Row);
        return EdictDispatchOutcome.HandledWith(BuildUpsertEntry(partitionKey, rowKey, box.Row));
    }

    OutboxEntry BuildUpsertEntry(string partitionKey, string rowKey, TRow row)
    {
        // The row type identity that travels with the effect is the
        // frozen [Alias] literal, captured here via TypeConverter.Format so the
        // string that survives a class rename is what the drain resolves with
        // TypeConverter.Parse. Replaces the previous AssemblyQualifiedName hop
        // that dead-lettered on rename or move.
        var typeConverter = _cachedTypeConverter ??= ServiceProvider.GetRequiredService<TypeConverter>();
        var serializer = _cachedSerializer ??= ServiceProvider.GetRequiredService<Serializer>();
        var effect = new UpsertRowEffect
        {
            TableName = TableName,
            PartitionKey = partitionKey,
            RowKey = rowKey,
            RowAlias = typeConverter.Format(typeof(TRow)),
            // Stage as object so the wire bytes carry the Orleans type id;
            // the drain decodes via Deserialize<object> and gets the concrete
            // row instance back without needing TRow at runtime.
            RowBytes = serializer.SerializeToArray<object>(row),
        };

        // Nest the deferred upsert under the live handle span as parent-child,
        // even when a crash-recovery drain runs much later.
        var current = Activity.Current;
        var traceParent = current?.BuildTraceParent();

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
