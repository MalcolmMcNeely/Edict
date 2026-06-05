using System.ComponentModel;

namespace Edict.Core.Correlation;

/// <summary>
/// The processed-correlation slot of the grain-state envelope: a bounded ring of
/// the most recently processed <c>EdictEvent.CorrelationId</c>s, persisted so a
/// read-your-writes read survives a projection deactivate/reactivate. Shape
/// mirrors the dedup ring (<c>Guid[]</c> + head + count); the ring advances on the
/// same atomic write as the dedup-ring commit, so tracking processed correlations
/// costs no extra write. Only projection roles populate this slot; every other
/// idempotent consumer leaves it null, so the cost for them is one empty reference.
/// <para>
/// Public because it rides as a slot on the public <c>GrainEnvelope&lt;TPayload&gt;</c>;
/// hidden from consumer IntelliSense because the consumer never types it. Persisted
/// state, so a frozen string-literal <c>[Alias]</c> survives a class rename.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[GenerateSerializer]
[Alias("CorrelationProgress")]
public sealed class CorrelationProgress
{
    [Id(0)]
    public Guid[] CorrelationIds { get; set; } = [];

    [Id(1)]
    public int Head { get; set; }

    [Id(2)]
    public int Count { get; set; }
}
