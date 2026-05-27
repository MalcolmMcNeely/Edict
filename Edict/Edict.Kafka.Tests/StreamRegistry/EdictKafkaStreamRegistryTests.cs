using System.Reflection;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Kafka.Internal;

using Xunit;

namespace Edict.Kafka.Tests.StreamRegistry;

/// <summary>
/// Targeted unit tests for the stream-registry seam — proves the discovery
/// rules a per-stream Kafka topology rides on (one Kafka topic per
/// <see cref="EdictStreamAttribute"/> domain name): only concrete events
/// deriving from <see cref="EdictEvent"/> contribute, duplicates collapse,
/// and the emitted order is stable so the consistent-ring queue balancer
/// cannot disagree between silos.
/// </summary>
public sealed class EdictKafkaStreamRegistryTests
{
    static EdictKafkaStreamRegistry Build(params Assembly[] assemblies) =>
        EdictKafkaStreamRegistry.FromAssemblies(assemblies);

    [Fact]
    public void StreamNames_ShouldEnumerateDistinctEdictStreamAttributeValues()
    {
        var registry = Build(typeof(EdictKafkaStreamRegistryTests).Assembly);

        // Two fixtures below carry "ScanAlpha" and "ScanBeta"; a duplicate
        // [EdictStream("ScanAlpha")] on a second event must collapse to a
        // single entry.
        Assert.Contains("ScanAlpha", registry.StreamNames);
        Assert.Contains("ScanBeta", registry.StreamNames);
        Assert.Equal(registry.StreamNames.Distinct().Count(), registry.StreamNames.Count);
    }

    [Fact]
    public void StreamNames_ShouldIgnoreAttributeOnNonEdictEventTypes()
    {
        // ScanIgnoredHelper carries [EdictStream] but does not derive from
        // EdictEvent — that scenario would be a bug, but the registry's
        // filter must catch it so a publisher cannot drag random tagged
        // classes onto the topic list.
        var registry = Build(typeof(EdictKafkaStreamRegistryTests).Assembly);

        Assert.DoesNotContain("ScanIgnored", registry.StreamNames);
    }

    [Fact]
    public void StreamNames_ShouldReturnAStableOrderAcrossInstances()
    {
        // Two silos sharing the same assembly set must enumerate queues in
        // the same order — the consistent-ring queue balancer assumes a
        // stable queue set, so any change here is a silent rebalance bug.
        var first = Build(typeof(EdictKafkaStreamRegistryTests).Assembly).StreamNames;
        var second = Build(typeof(EdictKafkaStreamRegistryTests).Assembly).StreamNames;

        Assert.Equal(first, second);
    }

    [EdictStream("ScanAlpha")]
    public sealed partial record ScanAlphaEvent : EdictEvent
    {
        [EdictRouteKey]
        public Guid Id { get; init; }
    }

    [EdictStream("ScanAlpha")]
    public sealed partial record ScanAlphaDuplicateEvent : EdictEvent
    {
        [EdictRouteKey]
        public Guid Id { get; init; }
    }

    [EdictStream("ScanBeta")]
    public sealed partial record ScanBetaEvent : EdictEvent
    {
        [EdictRouteKey]
        public Guid Id { get; init; }
    }

    // Non-EdictEvent type carrying the attribute — proves the registry's
    // base-type filter is what keeps random tagged classes off the topic list,
    // not the absence of the attribute (the analyzer already forbids untagged
    // EdictEvent types at compile time).
    [EdictStream("ScanIgnored")]
    public sealed class ScanIgnoredHelper;
}
