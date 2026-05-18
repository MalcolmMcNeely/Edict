using System.Reflection;

using Azure.Data.Tables;

namespace Edict.Core.TableStorage;

/// <summary>
/// Reflection-based mapper between a plain POCO and an Azure <see cref="TableEntity"/>.
/// Supports the primitive Azure Table Storage types (string, int, long, double, bool,
/// Guid, DateTime, DateTimeOffset, byte[]). ADR 0015: rows are plain POCOs; the
/// provider handles all Azure plumbing.
/// </summary>
internal static class AzureTablePocoMapper
{
    private static readonly BindingFlags PublicInstance =
        BindingFlags.Public | BindingFlags.Instance;

    internal static TableEntity ToTableEntity<T>(string partitionKey, string rowKey, T row)
        where T : class
    {
        var entity = new TableEntity(partitionKey, rowKey);
        foreach (var prop in typeof(T).GetProperties(PublicInstance).Where(p => p.CanRead))
            entity[prop.Name] = prop.GetValue(row);
        return entity;
    }

    internal static T FromTableEntity<T>(TableEntity entity) where T : class, new()
    {
        var instance = new T();
        foreach (var prop in typeof(T).GetProperties(PublicInstance).Where(p => p.CanWrite))
        {
            if (!entity.TryGetValue(prop.Name, out var value) || value is null)
                continue;
            try
            {
                prop.SetValue(instance, Convert.ChangeType(value, prop.PropertyType));
            }
            catch (InvalidCastException) { }
            catch (FormatException) { }
        }
        return instance;
    }
}
