using System;

using Edict.Analyzers.Interceptors;

using Xunit;

namespace Edict.Analyzers.Tests.Interceptors;

public class OriginSendTenantAnalyzerTests
{
    [Fact]
    public void EDICT024_ShouldRaise_OnBareOriginSend_OfTenantScopedCommand()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Sending;
            using Edict.Contracts.Tenancy;
            namespace Sample;
            [EdictTenantScoped]
            public readonly record struct EmployeeId(Guid Value);
            public sealed partial record AddEmployeeCommand(EmployeeId EmployeeId) : EdictCommand
            {
                [EdictRouteKey]
                public EmployeeId EmployeeId { get; init; } = EmployeeId;
            }
            public sealed class Caller
            {
                public Task<EdictCommandResult> Use(IEdictSender sender, Guid id)
                    => sender.SendAsync(new AddEmployeeCommand(new EmployeeId(id)));
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new OriginSendTenantAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT024", diagnostic.Id);

        var message = diagnostic.GetMessage();
        Assert.Contains("AddEmployeeCommand", message, StringComparison.Ordinal);
        Assert.Contains("tenant", message, StringComparison.Ordinal);
        Assert.Contains("resolver", message, StringComparison.Ordinal);
    }

    [Fact]
    public void EDICT024_ShouldNotRaise_OnBareOriginSend_OfPublicCommand()
    {
        // The marker is static source: a public aggregate's route-key type carries
        // no [EdictTenantScoped], so the selective analyzer stays silent.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Sending;
            namespace Sample;
            public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed class Caller
            {
                public Task<EdictCommandResult> Use(IEdictSender sender, Guid orderId)
                    => sender.SendAsync(new PlaceOrder(orderId));
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new OriginSendTenantAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT024_ShouldNotRaise_OnExplicitTenantOverload()
    {
        // The establishing-crossing overload supplies the tenant, so the bare-send
        // guard does not fire.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Sending;
            using Edict.Contracts.Tenancy;
            namespace Sample;
            [EdictTenantScoped]
            public readonly record struct EmployeeId(Guid Value);
            public sealed partial record AddEmployeeCommand(EmployeeId EmployeeId) : EdictCommand
            {
                [EdictRouteKey]
                public EmployeeId EmployeeId { get; init; } = EmployeeId;
            }
            public sealed class Caller
            {
                public Task<EdictCommandResult> Use(IEdictSender sender, Guid id)
                    => sender.SendAsync(new AddEmployeeCommand(new EmployeeId(id)), EdictTenantId.Of("acme"));
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new OriginSendTenantAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT024_ShouldNotRaise_WhenSiteSuppressed()
    {
        // SuppressMessage is the documented escape for a resolver-backed origin the
        // consumer has confirmed is attributed at runtime via AddEdictTenant.
        const string source = """
            using System;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Sending;
            using Edict.Contracts.Tenancy;
            namespace Sample;
            [EdictTenantScoped]
            public readonly record struct EmployeeId(Guid Value);
            public sealed partial record AddEmployeeCommand(EmployeeId EmployeeId) : EdictCommand
            {
                [EdictRouteKey]
                public EmployeeId EmployeeId { get; init; } = EmployeeId;
            }
            public sealed class Caller
            {
                [SuppressMessage("Edict", "EDICT024", Justification = "Resolver supplies the tenant at runtime")]
                public Task<EdictCommandResult> Use(IEdictSender sender, Guid id)
                    => sender.SendAsync(new AddEmployeeCommand(new EmployeeId(id)));
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new OriginSendTenantAnalyzer());

        Assert.Empty(diagnostics);
    }
}
