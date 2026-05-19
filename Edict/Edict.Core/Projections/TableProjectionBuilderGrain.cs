using Edict.Contracts.Events;
using Edict.Contracts.TableStorage;
using Edict.Core.TableStorage;

namespace Edict.Core.Projections;

/// <summary>
/// Projection builder whose read model lives in an external keyed store so grain
/// activation stays small regardless of how large the model grows (ADR 0012 / ADR 0015).
/// The backing store is supplied via <see cref="IEdictTableStoreFactory"/>; Azure is
/// one implementation — a future DynamoDB or in-memory provider implements the same seam.
/// The dedup ring stays in persisted grain state as usual — the row write and ring commit
/// are two non-atomic stores, and the resulting crash-window double-apply is accepted
/// until the Outbox ships.
/// </summary>
public abstract class EdictTableProjectionBuilderGrain<T>(IEdictTableStoreFactory writeStoreFactory) : EdictProjectionBuilderGrain
    where T : class, new()
{
    IEdictTableWriteStore<T>? _writeStore;

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
    /// Global-singleton projections override to use a different strategy.
    /// </summary>
    protected string DefaultPartitionKey => this.GetPrimaryKey().ToString();

    /// <summary>
    /// The row loaded (or freshly constructed) before each handler invocation.
    /// Modifications the handler makes to this instance are written back to the store
    /// after the handler returns.
    /// </summary>
    protected T CurrentRow { get; private set; } = new();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        _writeStore = await writeStoreFactory.CreateAsync<T>(TableName, cancellationToken);
    }

    /// <summary>
    /// Wraps every handler call with load-apply-writeback. The base
    /// <see cref="EdictProjectionBuilderGrain.DispatchEventAsync{TEvent}"/> default is a
    /// direct handler call; this override adds the store I/O around it.
    /// </summary>
    protected override async Task DispatchEventAsync<TEvent>(TEvent evt, Func<TEvent, Task> handler)
    {
        var partitionKey = DefaultPartitionKey;
        var rowKey = GetRowKey(evt);

        CurrentRow = await _writeStore!.GetAsync(partitionKey, rowKey) ?? new T();

        await handler(evt);

        await _writeStore!.UpsertAsync(partitionKey, rowKey, CurrentRow);
    }
}
