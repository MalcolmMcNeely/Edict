using System.ComponentModel;

namespace Edict.Core.Idempotency;

/// <summary>
/// The dedup-ring slot on <c>GrainEnvelope&lt;TPayload&gt;</c>. Public because
/// it rides on the public envelope; hidden from consumer IntelliSense because
/// the consumer never types this — the framework owns dedup-ring commits.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Alias("IdempotencyState")]
[GenerateSerializer]
public sealed class IdempotencyState
{
    [Id(0)]
    public Guid[] HandledEventIds { get; set; } = [];

    [Id(1)]
    public int Head { get; set; }

    [Id(2)]
    public int Count { get; set; }
}
