using System.Diagnostics.Metrics;

using Edict.Telemetry;

namespace Edict.Telemetry.Tests;

public sealed class EdictDiagnosticsMeterTests
{
    [Fact]
    public void Meter_ShouldExposeASingleMeter_NamedEdict()
    {
        Assert.NotNull(EdictDiagnostics.Meter);
        Assert.Equal(EdictDiagnostics.SourceName, EdictDiagnostics.Meter.Name);
    }

    [Fact]
    public void Meter_ShouldBeObservableViaMeterListener_ListeningToTheEdictSource()
    {
        var observed = new List<Instrument>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName)
                {
                    observed.Add(instrument);
                }
            },
        };
        listener.Start();

        var probe = EdictDiagnostics.Meter.CreateCounter<long>("edict.meter_listener_probe");

        Assert.Contains(observed, i => i.Name == "edict.meter_listener_probe");
    }
}
