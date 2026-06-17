using Edict.Contracts.Audit;
using Edict.Contracts.Sending;
using Edict.Contracts.Tenancy;
using Edict.Core.Audit;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Tenancy;
using Edict.Core.Serialization;
using Edict.Core.Tests.Grains;

using FluentValidation;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Serialization;
using Orleans.TestingHost;

using Xunit;

namespace Edict.Core.Tests.Audit;

[CollectionDefinition(Name)]
public sealed class AuditCaptureCollection : ICollectionFixture<AuditCaptureClusterFixture>
{
    public const string Name = "AuditCapture";
}

// Shared store instance the silo writes to and the test reads. Static because an
// Orleans test silo configurator is constructed by the runtime and cannot capture
// fixture instance state; tests scope reads by a unique counter id rather than
// relying on isolation.
static class AuditCaptureStoreHolder
{
    public static readonly RecordingAuditStore Instance = new();

    public static readonly RecordingAuditPayloadStore PayloadInstance = new();
}

// In-memory cluster with auditing on and an in-memory audit store, so a test can
// drive a command and read the captured, drained record the way a consumer would.
public sealed class AuditCaptureClusterFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;

    public RecordingAuditStore AuditStore => AuditCaptureStoreHolder.Instance;

    public RecordingAuditPayloadStore PayloadStore => AuditCaptureStoreHolder.PayloadInstance;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public Serializer Serializer =>
        Cluster.Client.ServiceProvider.GetRequiredService<Serializer>();

    public IGrainFactory GrainFactory => Cluster.GrainFactory;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public Task DisposeAsync() =>
        Cluster is not null ? Cluster.DisposeAsync().AsTask() : Task.CompletedTask;

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(CounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddEdict();
            siloBuilder.Services.AddEdictOutbox();
            siloBuilder.Services.AddEdictAudit(_ => CapturePrincipal);
            siloBuilder.Services.AddEdictTenant(() => CaptureTenant);
            siloBuilder.Services.AddSingleton<IEdictAuditStore>(AuditCaptureStoreHolder.Instance);
            siloBuilder.Services.AddSingleton<IEdictAuditPayloadStore>(AuditCaptureStoreHolder.PayloadInstance);
            siloBuilder.Services.AddSingleton<IValidator<RejectByValidatorCommand>, RejectByValidatorCommandValidator>();
            siloBuilder.WithAudit();

            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-state");
            siloBuilder.AddMemoryStreams("edict");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
            clientBuilder.Services.AddEdictAudit(() => CapturePrincipal);
            clientBuilder.Services.AddEdictTenant(() => CaptureTenant);
        }
    }

    // A constant edge principal, distinct from the AuditOrigin collection's shared
    // mutable static so the two collections never race each other in parallel.
    public static readonly EdictPrincipal CapturePrincipal = EdictPrincipal.Of("alice");

    // A constant edge tenant the resolver stamps onto every origin send, so a
    // captured record carries the tenant alongside the principal.
    public static readonly EdictTenantId CaptureTenant = EdictTenantId.Of("acme");
}

sealed class RejectByValidatorCommandValidator : AbstractValidator<RejectByValidatorCommand>
{
    public RejectByValidatorCommandValidator() =>
        RuleFor(command => command.CounterId)
            .Must(_ => false)
            .WithErrorCode("always_rejected")
            .WithMessage("Rejected for the audit-capture test.");
}
