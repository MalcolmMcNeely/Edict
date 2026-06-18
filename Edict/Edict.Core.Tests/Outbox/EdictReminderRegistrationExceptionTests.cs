using Edict.Core.Outbox;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Runtime;
using Orleans.Serialization;

using Xunit;

namespace Edict.Core.Tests.Outbox;

// The whole point of the typed exception is that it survives the Orleans message-serializer
// hop on the command paths where the registration runs inside the called grain. Without the
// [GenerateSerializer] codec it would surface to the caller as an opaque CodecNotFoundException.
// This pins the load-bearing property without standing up a cluster.
public sealed class EdictReminderRegistrationExceptionTests
{
    static Serializer BuildSerializer() =>
        new ServiceCollection()
            .AddSerializer(builder => builder.AddAssembly(typeof(EdictReminderRegistrationException).Assembly))
            .BuildServiceProvider()
            .GetRequiredService<Serializer>();

    [Fact]
    public void ShouldRoundTripTypeAndMessage_ThroughOrleansSerializer()
    {
        var serializer = BuildSerializer();
        var original = new EdictReminderRegistrationException("register", new OrleansException("still initializing"));

        var bytes = serializer.SerializeToArray(original);
        var roundTripped = serializer.Deserialize<EdictReminderRegistrationException>(bytes);

        Assert.IsType<EdictReminderRegistrationException>(roundTripped);
        Assert.Equal(original.Message, roundTripped.Message);
    }
}
