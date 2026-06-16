using Edict.Contracts.Audit;

namespace Edict.Core.Tests.Audit;

// A fault-injecting IEdictAuditStore for the drain-failure tests, kept separate
// from RecordingAuditStore so that store stays an honest always-succeeding oracle.
// ThrowOnNextAppend faults exactly the next AppendAsync and then clears, so a test
// can fail one drain and let the retry through. Every non-faulted call delegates to
// a wrapped RecordingAuditStore, so the same dedup-on-record-id and query behaviour
// backs the success path.
public sealed class FaultInjectingAuditStore : IEdictAuditStore
{
    readonly RecordingAuditStore _inner = new();

    public bool ThrowOnNextAppend { get; set; }

    public int Count => _inner.Count;

    public Task AppendAsync(IReadOnlyList<EdictAuditRecord> records, CancellationToken cancellationToken)
    {
        if (ThrowOnNextAppend)
        {
            ThrowOnNextAppend = false;
            throw new InvalidOperationException("Injected audit chain store fault.");
        }

        return _inner.AppendAsync(records, cancellationToken);
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, CancellationToken cancellationToken) =>
        _inner.ByEntityAsync(entityType, entityKey, cancellationToken);

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        _inner.ByEntityAsync(entityType, entityKey, from, to, cancellationToken);

    public Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(Guid correlationId, CancellationToken cancellationToken) =>
        _inner.ByCorrelationAsync(correlationId, cancellationToken);

    public Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        _inner.ByPrincipalAsync(principal, from, to, cancellationToken);
}
