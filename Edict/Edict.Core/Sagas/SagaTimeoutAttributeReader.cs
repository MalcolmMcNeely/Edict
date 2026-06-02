using System.Globalization;
using System.Reflection;

using Edict.Contracts.Sagas;

namespace Edict.Core.Sagas;

/// <summary>
/// Reads a saga class's <c>[EdictSagaTimeout]</c> declaration into the pure
/// <see cref="SagaTimeoutDeclaration"/> the <see cref="SagaDeadlineResolver"/>
/// consumes. The duration literal's validity is an authorship-time analyzer
/// concern; here it is parsed with invariant-culture <c>TimeSpan.Parse</c>.
/// </summary>
static class SagaTimeoutAttributeReader
{
    public static SagaTimeoutDeclaration Read(Type sagaType)
    {
        var attribute = sagaType.GetCustomAttribute<EdictSagaTimeoutAttribute>(inherit: true);
        if (attribute is null)
        {
            return SagaTimeoutDeclaration.None;
        }

        if (attribute.Unbounded)
        {
            return SagaTimeoutDeclaration.AsUnbounded;
        }

        if (string.IsNullOrEmpty(attribute.Duration))
        {
            return SagaTimeoutDeclaration.None;
        }

        return SagaTimeoutDeclaration.ForDuration(
            TimeSpan.Parse(attribute.Duration, CultureInfo.InvariantCulture));
    }
}
