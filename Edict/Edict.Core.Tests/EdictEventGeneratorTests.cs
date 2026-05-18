using static VerifyXunit.Verifier;

namespace Edict.Core.Tests;

public class EdictEventGeneratorTests
{
    private const string SampleEventConsumer = """
        using System;
        using Edict.Contracts.Commands;
        using Edict.Contracts.Events;
        using MessagePack;

        namespace Sample;

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Orders")]
        public sealed partial record OrderCancelledEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }
        """;

    [Fact]
    public Task Generator_emits_alias_declaration_per_concrete_event()
    {
        var generated = GeneratorTestHarness.RunEventGenerator(SampleEventConsumer)
            .Where(kvp => kvp.Key.EndsWith(".Alias.g.cs", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return Verify(generated);
    }
}
