using System.Diagnostics;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Edict.Telemetry.Tests;

[EdictStream("TagWriterTagged")]
file sealed partial record TaggedEvent(Guid Id, string Sku) : EdictEvent
{
    [EdictRouteKey]
    public Guid Id { get; init; } = Id;

    public string Sku { get; init; } = Sku;
}

[EdictStream("TagWriterUntagged")]
file sealed partial record UntaggedEvent(Guid Id) : EdictEvent
{
    [EdictRouteKey]
    public Guid Id { get; init; } = Id;
}

public class EventTagWritersTests
{
    [Fact]
    public void TryGet_ShouldReturnFalse_ForTypeWithNoRegistration()
    {
        var writers = new EventTagWriters(new Dictionary<Type, Action<EdictEvent, Activity>>());

        var found = writers.TryGet(typeof(UntaggedEvent), out var writer);

        Assert.False(found);
        Assert.NotNull(writer);
    }

    [Fact]
    public void TryGet_ShouldReturnRegisteredWriter_ForRegisteredType()
    {
        var capturedSku = string.Empty;
        var writers = new EventTagWriters(new Dictionary<Type, Action<EdictEvent, Activity>>
        {
            [typeof(TaggedEvent)] = (e, a) => a.SetTag("edict.sku", ((TaggedEvent)e).Sku),
        });

        var found = writers.TryGet(typeof(TaggedEvent), out var writer);

        Assert.True(found);
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => capturedSku = (string?)a.GetTagItem("edict.sku") ?? string.Empty,
        };
        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource("EventTagWritersTests");
        using var activity = source.StartActivity("test");
        Assert.NotNull(activity);
        writer(new TaggedEvent(Guid.NewGuid(), "SKU-UNIT-1"), activity!);
        activity!.Dispose();

        Assert.Equal("SKU-UNIT-1", capturedSku);
    }
}
