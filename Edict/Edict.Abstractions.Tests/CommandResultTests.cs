using System.Reflection;

using Edict.Abstractions;

using static VerifyXunit.Verifier;

namespace Edict.Abstractions.Tests;

public class CommandResultTests
{
    [Fact]
    public void A_rejected_result_carries_its_reasons_through_an_exhaustive_switch()
    {
        var reason = new RejectionReason("out_of_stock", "Item is out of stock.");
        CommandResult result = new CommandResult.Rejected([reason]);

        var extracted = result switch
        {
            CommandResult.Accepted => Array.Empty<RejectionReason>(),
            CommandResult.Rejected rejected => rejected.Reasons.ToArray(),
        };

        Assert.Equal([reason], extracted);
    }

    [Fact]
    public void Accepted_results_are_value_equal()
    {
        Assert.Equal(new CommandResult.Accepted(), new CommandResult.Accepted());
    }

    [Fact]
    public Task CommandResult_is_a_closed_hierarchy_of_exactly_Accepted_and_Rejected()
    {
        var baseType = typeof(CommandResult);

        var shape = new
        {
            IsAbstract = baseType.IsAbstract,
            ExternallyConstructible = baseType
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(constructor => !IsRecordCopyConstructor(constructor, baseType))
                .Any(constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly),
            Variants = baseType
                .GetNestedTypes()
                .Where(nested => nested.IsSubclassOf(baseType))
                .Select(nested => new { nested.Name, IsSealed = nested.IsSealed })
                .OrderBy(variant => variant.Name),
        };

        return Verify(shape);
    }

    // Records synthesize a protected copy constructor `T(T original)` used only
    // for cloning; it does not let external code derive a new variant because the
    // private parameterless constructor still blocks that. Exclude it so the
    // closed-ness assertion reflects real reachability, not a reflection artifact.
    private static bool IsRecordCopyConstructor(ConstructorInfo constructor, Type declaringType)
    {
        var parameters = constructor.GetParameters();
        return parameters is [{ } single] && single.ParameterType == declaringType;
    }
}
