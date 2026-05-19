namespace Edict.Core.Dedup;

[GenerateSerializer]
public sealed class DeduplicationState
{
    [Id(0)]
    public Guid[] Ring { get; set; } = [];

    [Id(1)]
    public int Head { get; set; }

    [Id(2)]
    public int Count { get; set; }
}
