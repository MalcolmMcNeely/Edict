using Edict.Contracts.Configuration;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Configuration;

// Pure-function tests over the marker-collection inspector. The
// inspector is the seam EdictWiringValidator uses to turn a DI-resolved
// IEnumerable<IEdictWiringMarker> into a list of missing-provider
// descriptions; failures are accumulated, not short-circuited, so a host
// missing both providers sees both calls named at startup.
public sealed class EdictWiringInspectorTests
{
    [Fact]
    public Task Inspect_ShouldReportBothProviders_WhenNoMarkersRegistered()
    {
        var missing = EdictWiringInspector.Inspect([]);

        return Verify(missing);
    }

    [Fact]
    public Task Inspect_ShouldReportOnlyPersistence_WhenStreamsMarkerIsPresent()
    {
        var missing = EdictWiringInspector.Inspect([new EdictStreamsProviderMarker()]);

        return Verify(missing);
    }

    [Fact]
    public Task Inspect_ShouldReportOnlyStreams_WhenPersistenceMarkerIsPresent()
    {
        var missing = EdictWiringInspector.Inspect([new EdictPersistenceProviderMarker()]);

        return Verify(missing);
    }

    [Fact]
    public Task Inspect_ShouldReportNothing_WhenBothMarkersArePresent()
    {
        var missing = EdictWiringInspector.Inspect([
            new EdictStreamsProviderMarker(),
            new EdictPersistenceProviderMarker(),
        ]);

        return Verify(missing);
    }
}
