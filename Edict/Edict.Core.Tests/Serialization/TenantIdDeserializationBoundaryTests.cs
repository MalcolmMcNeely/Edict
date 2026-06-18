using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;

using MessagePack;

using Xunit;

namespace Edict.Core.Tests.Serialization;

// The charset is a security boundary, so it must hold on the way *in* off the
// wire, not only when minted via Of: a value reconstructed from storage or a
// hostile peer must never carry the key delimiter into a composed key. These
// forge a command whose Tenant field bypasses Of — a surrogate of identical
// MessagePack shape — and prove the real contract serializer rejects it on
// deserialization, which only holds if the constructor is the deserialization
// entry point.
public sealed class TenantIdDeserializationBoundaryTests
{
    [Theory]
    [InlineData("acme|globex")]   // the composed-key delimiter — the forging vector
    [InlineData("acme corp")]     // a non-delimiter unsafe value (whitespace)
    public void Deserialize_CommandWithForgedTenant_Throws(string forgedTenant)
    {
        var forged = new ForgedTenantCommand { Tenant = new ForgedTenantId(forgedTenant) };
        var bytes = MessagePackSerializer.Serialize(forged);

        var exception = Assert.ThrowsAny<Exception>(
            () => MessagePackSerializer.Deserialize<PlaceOrderCommand>(bytes));

        Assert.IsType<EdictInvalidTenantIdException>(Unwrap(exception));
    }

    static Exception Unwrap(Exception exception) =>
        exception is MessagePackSerializationException && exception.InnerException is not null
            ? exception.InnerException
            : exception;

    // Mirrors EdictCommand + PlaceOrderCommand's keyAsPropertyName shape so the
    // serialized map's Tenant key is byte-identical to a real command's, but the
    // Tenant value bypasses EdictTenantId.Of.
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed record ForgedTenantCommand
    {
        public Guid CommandId { get; init; }

        public Guid CorrelationId { get; init; }

        public EdictPrincipal? Principal { get; init; }

        public ForgedTenantId? Tenant { get; init; }

        public Guid OrderId { get; init; }

        public string Sku { get; init; } = "";
    }

    [MessagePackObject(keyAsPropertyName: true)]
    public readonly record struct ForgedTenantId
    {
        public ForgedTenantId(string value) => Value = value;

        public string Value { get; init; }
    }
}
