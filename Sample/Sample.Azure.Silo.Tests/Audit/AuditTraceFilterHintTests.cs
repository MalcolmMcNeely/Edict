using Sample.Web.Components.Pages.Audit;

using Xunit;

namespace Sample.Azure.Silo.Tests.Audit;

/// <summary>
/// Consumer-facing coverage of the Sample audit page's trace-filter hint: the page
/// shows a visitor the exact Aspire-dashboard filter that selects every turn in a
/// conversation. The rendered expression must match the OTel attribute the framework
/// stamps on each turn span, so the literal here is the connection between the audit
/// spine and the trace view — it fails if the framework renames the tag.
/// </summary>
public sealed class AuditTraceFilterHintTests
{
    [Fact]
    public void ForConversation_RendersTheOtelAttributeFilter_AVisitorPastesIntoAspire()
    {
        // Arrange
        var conversationId = Guid.Parse("11112222-3333-4444-5555-666677778888");

        // Act
        var hint = AuditTraceFilterHint.ForConversation(conversationId);

        // Assert
        Assert.Equal("messaging.message.conversation_id = 11112222-3333-4444-5555-666677778888", hint);
    }
}
