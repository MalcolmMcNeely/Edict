using System.Reflection;
using Edict.Contracts.Commands;
using static VerifyXunit.Verifier;

namespace Edict.Contracts.Tests.Commands;

public class CommandResultTests
{
    [Fact]
    public void RejectedResult_ShouldCarryReasonsThroughExhaustiveSwitch()
    {
        var reason = new EdictRejectionReason("out_of_stock", "Item is out of stock.");
        EdictCommandResult result = new EdictCommandResult.Rejected([reason]);

        var extracted = result switch
        {
            EdictCommandResult.Accepted => Array.Empty<EdictRejectionReason>(),
            EdictCommandResult.Rejected rejected => rejected.Reasons.ToArray(),
        };

        Assert.Equal([reason], extracted);
    }

    [Fact]
    public void AcceptedResult_ShouldBeValueEqual()
    {
        Assert.Equal(new EdictCommandResult.Accepted(), new EdictCommandResult.Accepted());
    }

    [Fact]
    public Task CommandResult_ShouldBeClosedHierarchyOfExactlyAcceptedAndRejected()
    {
        var baseType = typeof(EdictCommandResult);

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
