using System.Reflection;

using Edict.Core.Commands;

using Orleans;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace Edict.Core.Tests.Tenancy;

// A hand-rolled IIncomingGrainCallContext for unit-testing the tenant isolation call
// filter without a cluster. Only the members the filter reads are functional — the
// target grain (for the command-handler scope check), the target id (whose key the
// filter parses), and Invoke (whose call signals the happy path proceeded). Everything
// else throws so an accidental dependency on an unmodelled member is loud, not silent.
sealed class FakeIncomingGrainCallContext(object grain, GrainId targetId) : IIncomingGrainCallContext
{
    public bool Invoked { get; private set; }

    public object Grain { get; } = grain;

    public GrainId TargetId { get; } = targetId;

    public Task Invoke()
    {
        Invoked = true;
        return Task.CompletedTask;
    }

    public GrainId? SourceId => throw new NotSupportedException();
    public IInvokable Request => throw new NotSupportedException();
    public MethodInfo InterfaceMethod => throw new NotSupportedException();
    public MethodInfo ImplementationMethod => throw new NotSupportedException();
    public GrainInterfaceType InterfaceType => throw new NotSupportedException();
    public string InterfaceName => throw new NotSupportedException();
    public string MethodName => throw new NotSupportedException();
    public IGrainContext TargetContext => throw new NotSupportedException();
    public Response? Response { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public object? Result { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
}

// A command-handler grain stand-in: implementing IEdictCommandHandler is what marks a
// grain as tenant-enforceable, so the filter's scope check sees this as a grain it guards.
sealed class FakeCommandHandlerGrain : IEdictCommandHandler
{
    public Task<Contracts.Commands.EdictCommandResult> DispatchAsync(Contracts.Commands.EdictCommand command) =>
        throw new NotSupportedException();
}

// A non-command grain (a projection builder rides streams, not direct calls): the
// filter must pass it through untouched even when its key carries a tenant.
sealed class FakeProjectionGrain : IGrainWithStringKey;
