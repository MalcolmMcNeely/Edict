using Edict.Telemetry;

namespace Sample.Web.Components.Pages.Audit;

public static class AuditTraceFilterHint
{
    // The value half mirrors what ActivityExtensions.SetEdictConversationId stamps on
    // every turn span (Guid in its default "D" form), so the expression a visitor copies
    // from the audit page selects exactly that conversation in the Aspire dashboard.
    public static string ForConversation(Guid conversationId) =>
        $"{SemanticConventions.Messaging.Tags.ConversationId} = {conversationId}";
}
