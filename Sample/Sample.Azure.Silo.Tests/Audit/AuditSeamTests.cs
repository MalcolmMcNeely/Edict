using System.Security.Cryptography;

using Edict.Contracts.Audit;
using Edict.Core.Audit;
using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Contracts.Orders.Events;
using Sample.Domain.Orders.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Audit;

/// <summary>
/// Consumer-facing coverage of the in-memory audit seam: with auditing turned on
/// through <see cref="EdictTestAppBuilder.WithAudit"/>, a command sent through the
/// in-memory <see cref="EdictTestApp"/> captures its C1 decision and E1 events,
/// drains deterministically on <see cref="EdictTestApp.Drain"/>, and is asserted
/// only through the consumer read surface (<see cref="EdictTestApp.Audit"/>) — the
/// queryable record, the chain verdict, the retrieved payload — never a private
/// field.
/// </summary>
public sealed class AuditSeamTests
{
    static string OrderEntityType => typeof(OrderCommandHandler).FullName!;

    [Fact]
    public async Task Audit_ShouldCaptureCommandAndEventRecords_AttributedToTheDefaultPrincipal()
    {
        // Arrange
        var orderId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithAudit());

        // Act
        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Drain();

        // Assert — placing an order captures one C1 command record plus one E1 event
        // record (it raises one event), in chain order, both attributed to the
        // default test principal a bare WithAudit() applies.
        var records = await app.Audit.ByEntityAsync(OrderEntityType, orderId.ToString());

        Assert.Equal(
            [EdictAuditKind.Command, EdictAuditKind.Event],
            records.Select(record => record.Kind));
        Assert.Equal([0L, 1L], records.Select(record => record.Sequence));
        Assert.Equal(EdictAuditOutcome.Accepted, records[0].Outcome);
        Assert.All(records, record => Assert.NotNull(record.Principal));
    }

    [Fact]
    public async Task ActAs_ShouldAttributeSubsequentSendsToThatPrincipal()
    {
        // Arrange
        var orderId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        var clerk = EdictPrincipal.Of("clerk-7");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithAudit());

        // Act
        app.ActAs(clerk);
        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-002"));
        await app.Drain();

        // Assert — both the C1 command record and the E1 event record are attributed
        // to the actor ActAs set, and the principal timeline finds them.
        var records = await app.Audit.ByEntityAsync(OrderEntityType, orderId.ToString());
        Assert.NotEmpty(records);
        Assert.All(records, record => Assert.Equal(clerk, record.Principal));

        var byPrincipal = await app.Audit.ByPrincipalAsync(clerk, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.Equal(records.Count, byPrincipal.Count);
    }

    [Fact]
    public async Task VerifyEntityChain_ShouldReportIntact_ThenCatchADeliberateTamper()
    {
        // Arrange
        var orderId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithAudit());

        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-003"));
        await app.Drain();

        // Assert — the freshly captured chain verifies clean.
        var intact = await app.Audit.VerifyEntityChainAsync(OrderEntityType, orderId.ToString());
        Assert.True(intact.IsIntact);
        Assert.Null(intact.BrokenAtSequence);

        // Act — rewrite the event record's body identity in place, the way an
        // attacker editing the store would, leaving its sealed hash untouched.
        var records = await app.Audit.ByEntityAsync(OrderEntityType, orderId.ToString());
        var eventRecord = records.Single(record => record.Kind == EdictAuditKind.Event);
        app.TamperWithAuditRecord(eventRecord with { MessageType = "Tampered.Event" });

        // Assert — verification now fails, naming the altered record's sequence.
        var tampered = await app.Audit.VerifyEntityChainAsync(OrderEntityType, orderId.ToString());
        Assert.False(tampered.IsIntact);
        Assert.Equal(eventRecord.Sequence, tampered.BrokenAtSequence);
    }

    [Fact]
    public async Task GetPayload_ShouldReturnTheCapturedBody_WithAHashMatchingTheRecord()
    {
        // Arrange
        var orderId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithAudit());

        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-004"));
        await app.Drain();

        // Act — retrieve the C1 command body behind its payload reference.
        var records = await app.Audit.ByEntityAsync(OrderEntityType, orderId.ToString());
        var commandRecord = records.Single(record => record.Kind == EdictAuditKind.Command);
        var body = await app.Audit.GetPayloadAsync(commandRecord.RecordId);

        // Assert — the retrieved bytes hash to exactly the PayloadHash sealed into
        // the chain record.
        Assert.NotEmpty(body.ToArray());
        Assert.Equal(commandRecord.PayloadHash, SHA256.HashData(body.Span));
    }

    [Fact]
    public async Task GetMessage_ShouldReturnTheTypedCommandAndEvent_TheHandlerCaptured()
    {
        // Arrange
        var orderId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithAudit());

        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-005"));
        await app.Drain();

        var records = await app.Audit.ByEntityAsync(OrderEntityType, orderId.ToString());

        // Act — read each captured body back as the concrete message the consumer authored.
        var commandMessage = await app.Audit.GetMessageAsync(records.Single(record => record.Kind == EdictAuditKind.Command));
        var eventMessage = await app.Audit.GetMessageAsync(records.Single(record => record.Kind == EdictAuditKind.Event));

        // Assert — the boxed objects are the consumer's concrete types, field-identical to what was placed.
        var command = Assert.IsType<PlaceOrderCommand>(commandMessage);
        Assert.Equal(orderId, command.OrderId);
        Assert.Equal("REF-005", command.CustomerReference);

        var placed = Assert.IsType<OrderPlacedEvent>(eventMessage);
        Assert.Equal(orderId, placed.OrderId);
    }

    [Fact]
    public async Task GetMessage_ShouldThrowCarryingTheRecordIdentity_WhenTheBodyCannotBeDeserialized()
    {
        // Arrange
        var orderId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .WithAudit());

        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-006"));
        await app.Drain();

        var records = await app.Audit.ByEntityAsync(OrderEntityType, orderId.ToString());
        var eventRecord = records.Single(record => record.Kind == EdictAuditKind.Event);

        // Act — read the event body under a record that claims it is a Command, so
        // the body no longer deserializes to the kind it is asked for.
        var misread = eventRecord with { Kind = EdictAuditKind.Command };

        // Assert — the typed read throws, naming the record and the captured type;
        // the bytes stay retrievable through GetPayloadAsync as the fallback.
        var exception = await Assert.ThrowsAsync<EdictAuditMessageDeserializationException>(
            () => app.Audit.GetMessageAsync(misread));
        Assert.Equal(eventRecord.RecordId, exception.RecordId);
        Assert.Equal(eventRecord.MessageType, exception.MessageType);
    }
}
