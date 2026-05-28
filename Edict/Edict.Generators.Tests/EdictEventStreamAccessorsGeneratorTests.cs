using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictEventStreamAccessorsGeneratorTests
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

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Payments")]
        public sealed partial record PaymentAuthorizedEvent(Guid PaymentId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid PaymentId { get; init; } = PaymentId;
        }
        """;

    [Fact]
    public Task EdictEventStreamAccessorsGenerator_ShouldEmitAccessorRegistrarPerConcreteEvent()
    {
        var generated = GeneratorTestHarness.RunEdictEventStreamAccessorsGenerator(SampleEventConsumer)
            .Where(kvp => kvp.Key.EndsWith("EdictEventStreamRegistrar.g.cs", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return Verify(generated);
    }
}
