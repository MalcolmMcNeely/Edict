using Edict.Contracts.Configuration;
using Edict.Core.Outbox;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Core.Tests.Outbox;

// AddEdictOutbox ships sensible defaults the consumer can selectively override
// (the framework's first configurable knob — ADR 0018 / 0022). Single behavioral
// facts, so targeted Asserts rather than a snapshot.
public sealed class OutboxServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEdictOutbox_ShouldRegisterSensibleDefaults_WhenNotConfigured()
    {
        var provider = new ServiceCollection().AddEdictOutbox().BuildServiceProvider();

        var options = provider.GetRequiredService<EdictOutboxOptions>();

        Assert.Equal(TimeSpan.FromSeconds(2), options.BaseDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), options.MaxDelay);
        Assert.Equal(8, options.MaxAttempts);
        Assert.Equal(0.2, options.JitterFraction);
    }

    [Fact]
    public void AddEdictOutbox_ShouldApplyConsumerOverrides_WhenConfigured()
    {
        var provider = new ServiceCollection()
            .AddEdictOutbox(o =>
            {
                o.MaxAttempts = 3;
                o.JitterFraction = 0;
                o.BaseDelay = TimeSpan.FromSeconds(10);
            })
            .BuildServiceProvider();

        var options = provider.GetRequiredService<EdictOutboxOptions>();

        Assert.Equal(3, options.MaxAttempts);
        Assert.Equal(0, options.JitterFraction);
        Assert.Equal(TimeSpan.FromSeconds(10), options.BaseDelay);
        // Untouched knobs keep their defaults.
        Assert.Equal(TimeSpan.FromMinutes(5), options.MaxDelay);
    }
}
