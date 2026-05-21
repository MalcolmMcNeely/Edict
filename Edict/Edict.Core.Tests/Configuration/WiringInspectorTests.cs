using Edict.Contracts.Configuration;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Configuration;

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
