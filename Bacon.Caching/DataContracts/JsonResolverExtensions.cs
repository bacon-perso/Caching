using System.Reflection;
using System.Text.Json.Serialization.Metadata;

namespace Bacon.Caching.DataContracts;

internal sealed class JsonResolverExtensions
{
    public static void IgnoreJsonIgnoreAttributesModifier(JsonTypeInfo jsonTypeInfo)
    {
        if (jsonTypeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        jsonTypeInfo.Properties.Clear();

        PropertyInfo[] propertyInfos = jsonTypeInfo.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (PropertyInfo propertyInfo in propertyInfos)
        {
            if (propertyInfo.GetIndexParameters().Length > 0 || !propertyInfo.CanRead)
            {
                continue; // skip indexers / write-only props
            }

            JsonPropertyInfo jsonPropertyInfo = jsonTypeInfo.CreateJsonPropertyInfo(propertyInfo.PropertyType, propertyInfo.Name);
            jsonPropertyInfo.Get = propertyInfo.GetValue;

            if (propertyInfo.CanWrite)
            {
                jsonPropertyInfo.Set = propertyInfo.SetValue;
            }

            jsonTypeInfo.Properties.Add(jsonPropertyInfo);
        }
    }
}