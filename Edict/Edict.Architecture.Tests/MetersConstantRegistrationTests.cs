using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.CompilerServices;

using Edict.Core.Commands;
using Edict.Telemetry;

using Xunit;

namespace Edict.Architecture.Tests;

/// <summary>
/// Every <c>SemanticConventions.*.Meters.*</c> constant must map to an
/// instrument actually registered against <see cref="EdictDiagnostics.Meter"/>.
/// A dead constant — one declared but never wired into a CreateCounter /
/// CreateHistogram call — would silently break dashboards and alert recipes
/// that reference the name; this fact catches the drift at build time.
/// </summary>
public class MetersConstantRegistrationTests
{
    [Fact]
    public void EveryMetersConstant_ShouldMapToARegisteredInstrument()
    {
        var registered = CaptureRegisteredInstruments();
        var constants = CollectMeterNameConstants();

        Assert.NotEmpty(constants);

        var missing = constants.Where(c => !registered.Contains(c.value))
            .Select(c => $"{c.path} = \"{c.value}\"")
            .ToArray();

        Assert.True(missing.Length == 0,
            "These Meters.* constants do not correspond to any instrument created "
            + $"against EdictDiagnostics.Meter:\n  - {string.Join("\n  - ", missing)}");
    }

    static HashSet<string> CaptureRegisteredInstruments()
    {
        var registered = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, _) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName)
                {
                    lock (registered) { registered.Add(inst.Name); }
                }
            },
        };
        listener.Start();

        foreach (var type in InstrumentHostingTypes())
        {
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }

        return registered;
    }

    static IEnumerable<Type> InstrumentHostingTypes()
    {
        var assemblies = new[]
        {
            typeof(EdictDiagnostics).Assembly,
            typeof(EdictCommandHandler).Assembly,
        };

        var instrumentTypes = new[] { typeof(Counter<>), typeof(Histogram<>), typeof(ObservableCounter<>), typeof(ObservableGauge<>) };

        foreach (var asm in assemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                if (type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(f => f.FieldType.IsGenericType
                        && instrumentTypes.Contains(f.FieldType.GetGenericTypeDefinition())))
                {
                    yield return type;
                }
            }
        }
    }

    static List<(string path, string value)> CollectMeterNameConstants()
    {
        var collected = new List<(string, string)>();
        Walk(typeof(SemanticConventions), "SemanticConventions", collected);
        return collected;
    }

    static void Walk(Type t, string path, List<(string, string)> sink)
    {
        if (t.Name == "Meters")
        {
            foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (field.IsLiteral && field.FieldType == typeof(string))
                {
                    sink.Add(($"{path}.{field.Name}", (string)field.GetRawConstantValue()!));
                }
            }
        }

        foreach (var nested in t.GetNestedTypes(BindingFlags.Public))
        {
            Walk(nested, $"{path}.{nested.Name}", sink);
        }
    }
}
