using Edict.Contracts.Configuration;
using Edict.Contracts.Persistence;
using Edict.Contracts.Projections;
using Edict.Core.Correlation;
using Edict.Core.Idempotency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Edict.Core.Projections;

/// <summary>
/// Abstract root of the projection-builder species and the home of the
/// read-your-writes mechanism. Generic over the payload slot so a species may
/// keep its read model in grain state (<see cref="EdictProjectionBuilder{TProjection}"/>,
/// which closes this on its projection type) or in an external store
/// (<see cref="EdictListProjectionBuilder{TListProjection}"/>, which closes this on
/// <see cref="EdictUnit"/>). The dispatch seam (<c>DispatchEventAsync</c>) is
/// overridden here to stash the event's correlation and call the handler with no
/// staged effect, giving read-your-writes to every species; the List species
/// overrides it further for load-apply-writeback.
/// This root carries the grain read surface (<see cref="IEdictProjectionBuilder"/>):
/// the cursor wait and tri-state assembly live here so every concrete species
/// answers reads through the same mechanism, while the actual read is delegated to
/// <see cref="ReadRowAsync"/> / <see cref="ReadPartitionAsync"/> / <see cref="ReadAsync"/>,
/// which a species overrides for the reads that apply to it. The reads a species
/// does not support throw <see cref="EdictUnsupportedProjectionReadException"/>.
/// <para>
/// The processed-correlation ring advances on the same atomic write as the
/// dedup-ring commit (<see cref="ApplyConsumerProgress"/>), and the in-memory
/// wait mirror is signalled only after that write has drained
/// (<see cref="MarkConsumerProgressDrained"/>), so a <c>CursorReached</c> answer
/// implies the read model is readable.
/// </para>
/// </summary>
public abstract class EdictProjectionBuilderBase<TPayload> : EdictIdempotencyBase<TPayload>, IEdictProjectionBuilder
    where TPayload : IEdictPersistedState, new()
{
    readonly CursorWaitCoordinator _coordinator = new();
    TimeProvider? _clock;
    EdictOptions? _options;
    Guid[]? _seededRing;
    Guid _pendingCorrelationId;
    CorrelationRing.Revert _correlationRevert;
    bool _correlationApplied;

    TimeProvider Clock => _clock ??= ServiceProvider.GetRequiredService<TimeProvider>();

    EdictOptions Options => _options ??= ServiceProvider.GetService<IOptions<EdictOptions>>()?.Value ?? new EdictOptions();

    /// <summary>
    /// Maximum number of distinct correlation ids this projection remembers for
    /// read-your-writes. Resolved once and cached: the ring advances on the
    /// per-event hot path, so a DI lookup per event is wasted work.
    /// </summary>
    int CorrelationWindowSize => Options.CorrelationWindowSize;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // Seed the wait mirror from the persisted ring after drain-on-activation
        // has made the store consistent, so a post-reactivation read of a
        // still-remembered correlation answers CursorReached immediately.
        SeedCoordinatorFromState();
    }

    /// <inheritdoc />
    public async Task<ProjectionReadResult<object?>> EdictReadRowAsync(string rowKey, Guid? afterCorrelationId, TimeSpan? timeout)
    {
        var status = await AwaitCursorAsync(afterCorrelationId, timeout);
        var row = await ReadRowAsync(rowKey);
        return new ProjectionReadResult<object?>(row, status);
    }

    /// <inheritdoc />
    public async Task<ProjectionReadResult<IReadOnlyList<object>>> EdictReadPartitionAsync(Guid? afterCorrelationId, TimeSpan? timeout)
    {
        var status = await AwaitCursorAsync(afterCorrelationId, timeout);
        var rows = await ReadPartitionAsync();
        return new ProjectionReadResult<IReadOnlyList<object>>(rows, status);
    }

    /// <inheritdoc />
    public async Task<ProjectionReadResult<object?>> EdictReadAsync(Guid? afterCorrelationId, TimeSpan? timeout)
    {
        var status = await AwaitCursorAsync(afterCorrelationId, timeout);
        var value = await ReadAsync();
        return new ProjectionReadResult<object?>(value, status);
    }

    /// <summary>Point-gets the row at <paramref name="rowKey"/>. Overridden by the List species.</summary>
    protected virtual Task<object?> ReadRowAsync(string rowKey) =>
        throw new EdictUnsupportedProjectionReadException(GetType());

    /// <summary>Returns every row in this projection's partition. Overridden by the List species.</summary>
    protected virtual Task<IReadOnlyList<object>> ReadPartitionAsync() =>
        throw new EdictUnsupportedProjectionReadException(GetType());

    /// <summary>Returns the whole in-grain projection payload. Overridden by the State species.</summary>
    protected virtual Task<object?> ReadAsync() =>
        throw new EdictUnsupportedProjectionReadException(GetType());

    /// <summary>
    /// Stashes the event's correlation so the commit advances the
    /// read-your-writes ring for it, then runs the handler with no staged effect.
    /// The List species overrides this to add load-apply-writeback; the State
    /// species needs no override because mutating the payload is already durable
    /// on the inline commit.
    /// </summary>
    protected override async Task<EdictDispatchOutcome> DispatchEventAsync<TEvent>(TEvent edictEvent, Func<TEvent, Task> handler)
    {
        StashCorrelation(edictEvent.ConversationId);
        await handler(edictEvent);
        return EdictDispatchOutcome.HandledWithNoEffect;
    }

    /// <summary>
    /// Captures the correlation the current event carries so the commit advances
    /// the processed-correlation ring for it. Called by the dispatch seam before
    /// the handler runs, so the value is in hand when the commit runs.
    /// </summary>
    private protected void StashCorrelation(Guid correlationId) => _pendingCorrelationId = correlationId;

    async Task<EdictReadStatus> AwaitCursorAsync(Guid? afterCorrelationId, TimeSpan? timeout)
    {
        if (afterCorrelationId is not { } correlationId || correlationId == Guid.Empty)
        {
            return EdictReadStatus.Immediate;
        }

        var effectiveTimeout = CursorWaitCoordinator.ResolveTimeout(timeout, Options.ProjectionReadTimeout);

        // Caller cancellation is honoured at the reader facade before the grain
        // hop; an in-flight bounded wait always terminates on its own clock, and
        // an explicit-indefinite wait is pinned by this in-flight call.
        return await _coordinator.WaitAsync(correlationId, effectiveTimeout, Clock, CancellationToken.None);
    }

    private protected override void ApplyConsumerProgress()
    {
        _correlationApplied = false;
        if (_pendingCorrelationId == Guid.Empty)
        {
            return;
        }

        EnsureCorrelationWindow();
        _correlationRevert = CorrelationRing.Apply(State.Correlation!, CorrelationWindowSize, _pendingCorrelationId);
        _correlationApplied = true;
    }

    private protected override void RollbackConsumerProgress()
    {
        if (_correlationApplied)
        {
            CorrelationRing.RollBack(State.Correlation!, _correlationRevert);
            _correlationApplied = false;
        }
    }

    private protected override void MarkConsumerProgressDrained()
    {
        if (_pendingCorrelationId != Guid.Empty)
        {
            _coordinator.MarkProcessed(_pendingCorrelationId);

            // Clear it so a later commit that staged no fresh correlation (e.g. the
            // deferred claim-check intake) cannot re-mark this one.
            _pendingCorrelationId = Guid.Empty;
        }
    }

    void SeedCoordinatorFromState()
    {
        if (State.Correlation is { } ring)
        {
            _coordinator.Activate(ring.CorrelationIds, ring.Head, ring.Count);
            _seededRing = ring.CorrelationIds;
        }
        else
        {
            _coordinator.Activate([], 0, 0);
            _seededRing = null;
        }
    }

    void EnsureCorrelationWindow()
    {
        State.Correlation ??= new CorrelationProgress();
        if (State.Correlation.CorrelationIds.Length != CorrelationWindowSize)
        {
            State.Correlation.CorrelationIds = new Guid[CorrelationWindowSize];
            State.Correlation.Head = 0;
            State.Correlation.Count = 0;
        }

        // Re-seed the wait mirror when the ring array reference changes (first
        // populate, or a window-size change swaps the array); steady state hits
        // the reference-equal early-out.
        if (_seededRing is null || !ReferenceEquals(_seededRing, State.Correlation.CorrelationIds))
        {
            _coordinator.Activate(State.Correlation.CorrelationIds, State.Correlation.Head, State.Correlation.Count);
            _seededRing = State.Correlation.CorrelationIds;
        }
    }
}
