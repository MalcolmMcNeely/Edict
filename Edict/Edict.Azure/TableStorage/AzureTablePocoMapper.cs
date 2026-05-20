using System.Reflection;
using Azure.Data.Tables;

namespace Edict.Azure.TableStorage;

internal static class AzureTablePocoMapper
{
    static readonly BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    internal static TableEntity ToTableEntity<T>(string partitionKey, string rowKey, T row)
        where T : class
        => ToTableEntity(partitionKey, rowKey, (object)row);

    internal static TableEntity ToTableEntity(string partitionKey, string rowKey, object row)
    {
        var entity = new TableEntity(partitionKey, rowKey);
        foreach (var prop in row.GetType().GetProperties(PublicInstance).Where(p => p.CanRead))
        {
            var value = prop.GetValue(row);
            // Azure Tables rejects enum properties on POCO upsert; store the
            // enum name as a string so operators see human-readable values and
            // round-trip is symmetric with FromTableEntity below.
            if (value is Enum enumValue)
            {
                entity[prop.Name] = enumValue.ToString();
                continue;
            }

            entity[prop.Name] = value;
        }

        return entity;
    }

    internal static T FromTableEntity<T>(TableEntity entity) where T : class, new()
    {
        var instance = new T();
        foreach (var prop in typeof(T).GetProperties(PublicInstance).Where(p => p.CanWrite))
        {
            if (!entity.TryGetValue(prop.Name, out var value) || value is null)
            {
                continue;
            }

            try
            {
                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (targetType.IsEnum)
                {
                    prop.SetValue(instance, Enum.Parse(targetType, (string)value));
                    continue;
                }

                prop.SetValue(instance, Convert.ChangeType(value, targetType));
            }
            catch (InvalidCastException) { }
            catch (FormatException) { }
            catch (ArgumentException) { }
        }
        return instance;
    }
}
