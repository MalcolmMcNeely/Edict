using System.Text.Json.Serialization;

namespace Edict.Benchmarks.Throughput.Saturation;

/// <summary>
/// Which projection species a saturation pass measured — the dimension the
/// side-by-side EPS table is split on. <see cref="List"/> is the external keyed
/// store drained at-least-once; <see cref="State"/> is the in-grain payload
/// committed inline with the dedup ring. Serialised as its name so the
/// per-substrate summary JSON stays human-readable.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProjectionSpecies>))]
public enum ProjectionSpecies
{
    List,
    State,
}
