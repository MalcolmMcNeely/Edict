using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EventTagWritersGeneratorTests
{
    [Fact]
    public Task EventTagWritersGenerator_ShouldEmitWriterPerEventWithTelemeterizedPrimitive()
    {
        var source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Contracts.Telemetry;
            using MessagePack;

            namespace Sample;

            // One Telemeterized primitive
            [MessagePackObject(keyAsPropertyName: true)]
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId, string Sku) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;

                [EdictTelemeterized]
                public string Sku { get; init; } = Sku;
            }

            // Multiple Telemeterized primitives
            [MessagePackObject(keyAsPropertyName: true)]
            [EdictStream("Orders")]
            public sealed partial record OrderShippedEvent(Guid OrderId, string CarrierName, int PackageCount) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;

                [EdictTelemeterized]
                public string CarrierName { get; init; } = CarrierName;

                [EdictTelemeterized]
                public int PackageCount { get; init; } = PackageCount;
            }

            // None — should not appear in the registrar
            [MessagePackObject(keyAsPropertyName: true)]
            [EdictStream("Payments")]
            public sealed partial record PaymentAuthorizedEvent(Guid PaymentId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid PaymentId { get; init; } = PaymentId;
            }
            """;

        var generated = GeneratorTestHarness.RunEventTagWritersGenerator(source);

        return Verify(generated);
    }

    [Fact]
    public Task EventTagWritersGenerator_ShouldHonourInheritedTelemeterizedFromBaseEvent()
    {
        var source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Contracts.Telemetry;
            using MessagePack;

            namespace Sample;

            public abstract record DomainEvent(Guid AggregateId, string TenantId) : EdictEvent
            {
                [EdictTelemeterized]
                public string TenantId { get; init; } = TenantId;
            }

            [MessagePackObject(keyAsPropertyName: true)]
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid AggregateId, string TenantId)
                : DomainEvent(AggregateId, TenantId)
            {
                [EdictRouteKey]
                public Guid AggregateId { get; init; } = AggregateId;
            }
            """;

        var generated = GeneratorTestHarness.RunEventTagWritersGenerator(source);

        return Verify(generated);
    }
}
