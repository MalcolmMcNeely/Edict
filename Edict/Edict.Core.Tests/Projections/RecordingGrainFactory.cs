using Edict.Contracts.Events;
using Edict.Contracts.Projections;
using Edict.Core.Projections;

using Orleans;
using Orleans.Runtime;

namespace Edict.Core.Tests.Projections;

// A hand-rolled IGrainFactory that records the string key a projection read addressed,
// so a unit test can assert the reader folded the ambient tenant into the partition
// without standing up a cluster. Only the string-key GetGrain the reader uses is
// functional; every other overload throws so an unmodelled dependency is loud.
sealed class RecordingGrainFactory : IGrainFactory
{
    public string? LastGrainKey { get; private set; }

    public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null)
        where TGrainInterface : IGrainWithStringKey
    {
        LastGrainKey = primaryKey;
        return (TGrainInterface)(object)new FakeProjectionBuilderGrain();
    }

    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey => throw new NotSupportedException();
    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey => throw new NotSupportedException();
    public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey => throw new NotSupportedException();
    public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey => throw new NotSupportedException();
    public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable => throw new NotSupportedException();
    public IAddressable GetGrain(GrainId grainId) => throw new NotSupportedException();
    public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => throw new NotSupportedException();
    public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => throw new NotSupportedException();
    public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => throw new NotSupportedException();
    public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => throw new NotSupportedException();
    public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string keyExtension) => throw new NotSupportedException();
    public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string keyExtension) => throw new NotSupportedException();
    public IAddressable GetGrain(Type grainInterfaceType, IdSpan grainKey) => throw new NotSupportedException();
    public IAddressable GetGrain(Type grainInterfaceType, IdSpan grainKey, string grainClassNamePrefix) => throw new NotSupportedException();
    public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
    public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
}

// A do-nothing projection grain the recording factory hands back; the reader only
// awaits its reads, so canned empty results are enough to exercise the key composition.
sealed class FakeProjectionBuilderGrain : IEdictProjectionBuilder
{
    public Task<ProjectionReadResult<object?>> EdictReadRowAsync(string rowKey, Guid? afterCorrelationId, TimeSpan? timeout) =>
        Task.FromResult(new ProjectionReadResult<object?>(null, EdictReadStatus.Immediate));

    public Task<ProjectionReadResult<IReadOnlyList<object>>> EdictReadPartitionAsync(Guid? afterCorrelationId, TimeSpan? timeout) =>
        Task.FromResult(new ProjectionReadResult<IReadOnlyList<object>>([], EdictReadStatus.Immediate));

    public Task<ProjectionReadResult<object?>> EdictReadAsync(Guid? afterCorrelationId, TimeSpan? timeout) =>
        Task.FromResult(new ProjectionReadResult<object?>(null, EdictReadStatus.Immediate));

    public Task OnEdictEventAsync(EdictEvent edictEvent) => Task.CompletedTask;
}
