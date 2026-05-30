using Edict.Hosting;
using Orleans.Hosting;

namespace FixtureLibrary;

public static class Program
{
    public static void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddEdict()
            .AddEdictAzurePersistence()
            .AddEdictAzureStreams();
    }
}
