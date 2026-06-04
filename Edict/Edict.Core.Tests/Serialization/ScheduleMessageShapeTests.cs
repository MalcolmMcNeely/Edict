using Edict.Contracts.Schedules;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Serialization;

// A schedule persists its message as bytes and deserialises it polymorphically
// on each fire, so the message must round-trip through the Orleans serializer
// from its abstract base exactly as an EdictCommand does — this is the ADR-0007
// drift guard for the schedule-message wire shape.
public sealed class ScheduleMessageShapeTests
{
    static readonly Guid OrderId = new("eeeeeeee-0000-0000-0000-000000000007");

    static Serializer BuildSerializer()
    {
        var serviceProvider = new ServiceCollection()
            .AddSerializer(builder => builder
                .AddAssembly(typeof(ScheduleMessageShapeTests).Assembly)
                .AddEdictContractSerializer())
            .BuildServiceProvider();

        return serviceProvider.GetRequiredService<Serializer>();
    }

    [Fact]
    public Task EdictScheduleMessage_ShouldRoundTripPolymorphically_FromAbstractBase()
    {
        var serializer = BuildSerializer();
        EdictScheduleMessage message = new PingSchedule(OrderId);

        var bytes = serializer.SerializeToArray(message);
        var roundTripped = serializer.Deserialize<EdictScheduleMessage>(bytes);

        return Verify(roundTripped).DontScrubGuids();
    }

    public sealed record PingSchedule(Guid OrderId) : EdictScheduleMessage;
}
