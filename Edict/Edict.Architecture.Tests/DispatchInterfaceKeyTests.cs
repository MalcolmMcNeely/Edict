using Edict.Core.Commands;
using Edict.Core.Idempotency;
using Edict.Core.Schedules;

using Orleans;

using Xunit;

namespace Edict.Architecture.Tests;

// R1: every consumer's dispatch grain binds to a string key, always — single-tenant
// included. It is the one-way door the tenant fold rests on, since the composed
// {tenant}|{guid} key cannot live in a compile-time-fixed Guid key. A regression to
// IGrainWithGuidKey would re-Guid the persisted grain-state key and break the fold.
public sealed class DispatchInterfaceKeyTests
{
    [Theory]
    [InlineData(typeof(IEdictCommandHandler))]
    [InlineData(typeof(IEdictEventConsumer))]
    [InlineData(typeof(IEdictScheduleFireable))]
    public void DispatchInterface_BindsToStringKey(Type dispatchInterface)
    {
        Assert.True(typeof(IGrainWithStringKey).IsAssignableFrom(dispatchInterface));
        Assert.False(typeof(IGrainWithGuidKey).IsAssignableFrom(dispatchInterface));
    }
}
