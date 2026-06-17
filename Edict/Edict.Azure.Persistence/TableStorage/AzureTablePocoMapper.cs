using System.Reflection;
using Azure.Data.Tables;

using Edict.Contracts.Tenancy;

namespace Edict.Azure.Persistence.TableStorage;

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

            // A tenant tag is an EdictTenantId, not one of Azure Tables' native
            // column types; store its safe-everywhere string so the row carries the
            // wall and round-trips symmetric with FromTableEntity below.
            if (value is EdictTenantId tenantId)
            {
                entity[prop.Name] = tenantId.Value;
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

                if (targetType == typeof(EdictTenantId))
                {
                    prop.SetValue(instance, new EdictTenantId((string)value));
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
