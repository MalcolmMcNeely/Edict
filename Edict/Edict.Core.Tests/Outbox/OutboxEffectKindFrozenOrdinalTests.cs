using Edict.Core.Outbox;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Outbox;

// freeze guard: OutboxEntry is persisted Orleans grain state and
// Orleans serialises enums by ordinal, so reordering or removing a value
// silently corrupts every existing dead-letter / outbox row in production.
// The snapshot pins name→ordinal so any reorder fails CI.

public sealed class OutboxEffectKindFrozenOrdinalTests
{
    [Fact]
    public Task OutboxEffectKind_ShouldHaveFrozenOrdinals() =>
        Verify(
            Enum.GetValues<OutboxEffectKind>()
                .Select(value => new { Name = value.ToString(), Ordinal = (int)value }));
}
