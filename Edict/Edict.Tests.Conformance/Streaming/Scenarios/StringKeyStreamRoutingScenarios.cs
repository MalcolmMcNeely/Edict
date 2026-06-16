using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// The string-key plus implicit-subscription routing gate (the multi-tenancy
/// uniform-string-key spike): an event routed through a stream whose key is a composed
/// <c>"{tenant}|{guid}"</c> string activates an implicitly-subscribed,
/// string-keyed grain with that exact key recovered, not mangled, split, or
/// defaulted by the stream-id mapper. Bound on every real streaming provider so
/// the routing the uniform-string-key design rests on is proven against real
/// tech before the breaking keying change is committed.
/// </summary>
public abstract class StringKeyStreamRoutingScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected StringKeyStreamRoutingScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ComposedTenantStreamKey_ShouldActivateProjectionWithKeyRecoveredExactly()
    {
        // Arrange
        var composedKey = $"acme|{Guid.NewGuid():N}";
        const int probeValue = 7;

        // Act
        var publisher = _fixture.GrainFactory.GetGrain<IStringKeyRoutingPublisher>(composedKey);
        await publisher.PublishProbeAsync(probeValue);

        // Assert
        var projection = _fixture.GrainFactory.GetGrain<IStringKeyRoutingProjectionAccess>(composedKey);
        var capture = await WaitForCaptureAsync(projection);
        Assert.NotNull(capture);
        Assert.Equal(composedKey, capture.ActivatedKey);
        Assert.Equal(probeValue, capture.Value);
    }

    static async Task<StringKeyRoutingCapture?> WaitForCaptureAsync(IStringKeyRoutingProjectionAccess projection)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var capture = await projection.GetCaptureAsync();
            if (capture is not null)
            {
                return capture;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return null;
    }
}
