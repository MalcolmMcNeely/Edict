using Edict.Contracts.Configuration;
using Edict.Core.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Edict.Core.Tests.Outbox;

public sealed class OutboxServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEdictOutbox_ShouldRegisterSensibleDefaults_WhenNotConfigured()
    {
        var provider = new ServiceCollection().AddEdictOutbox().BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<EdictOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(2), options.OutboxBaseDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), options.OutboxMaxDelay);
        Assert.Equal(8, options.OutboxMaxAttempts);
        Assert.Equal(0.2, options.OutboxJitterFraction);
    }

    [Fact]
    public void AddEdictOutbox_ShouldApplyConsumerOverrides_WhenConfigured()
    {
        var provider = new ServiceCollection()
            .AddEdictOutbox(o =>
            {
                o.OutboxMaxAttempts = 3;
                o.OutboxJitterFraction = 0;
                o.OutboxBaseDelay = TimeSpan.FromSeconds(10);
            })
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<EdictOptions>>().Value;

        Assert.Equal(3, options.OutboxMaxAttempts);
        Assert.Equal(0, options.OutboxJitterFraction);
        Assert.Equal(TimeSpan.FromSeconds(10), options.OutboxBaseDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), options.OutboxMaxDelay);
    }
}
