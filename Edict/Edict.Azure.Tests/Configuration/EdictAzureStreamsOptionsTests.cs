using static VerifyXunit.Verifier;

namespace Edict.Azure.Tests.Configuration;

public sealed class EdictAzureStreamsOptionsTests
{
    [Fact]
    public Task Construct_ShouldExposeDocumentedDefaults()
    {
        var options = new EdictAzureStreamsOptions();

        // NumQueues sits above Orleans' default of 8, aligning with Edict's
        // scale-default stance shared by QueuePollingPeriod = 10 ms.
        Assert.Equal(16, options.NumQueues);

        return Verify(options);
    }
}
