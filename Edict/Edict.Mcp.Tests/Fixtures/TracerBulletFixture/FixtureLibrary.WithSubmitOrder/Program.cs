using Edict.Core;
using Orleans.Hosting;

namespace FixtureLibrary.WithSubmitOrder;

public static class Program
{
    public static void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddEdict();
    }
}
