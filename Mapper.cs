using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Digdir.BDB.Dialogporten.ServiceProvider;

public static class Mapper
{
    // Amund Note:
    //  * [ ] Siden Mapper er stateless skal den også være thread safe
    //  * [ ] Vurder Wrapper class istedet for object i Dict
    //      * [ ] i.e Record(object value, Type type).
    //            Kan gjøre parsing av Dict lettere siden Blazor vet hvilken type den er og kan fylle in rett
    //  * [ ] Finnes det mappere som gjør dette for meg allerede?
    //  * [ ] Feil håndtering? Return null? return default? throw?

    private static object? FooBar(PropertyInfo fieldInfo, List<Dictionary<string, object?>> collection)
    {
        if (!fieldInfo.PropertyType.IsGenericType)
        {
            return null;
        }
        var genericArguments = fieldInfo.PropertyType.GetGenericArguments().FirstOrDefault();
        var foo = collection.Select(o => Map(genericArguments!, o)).ToList();
        return foo.Count == 0 ? null : foo;

    }

    public static T? Map<T>(Dictionary<string, object?> data) where T : class, new()
    {
        return (T?)Map(typeof(T), data);
    }
    private static object? Map(Type destinationType, Dictionary<string, object?> data)
    {
        var destination = Activator.CreateInstance(destinationType);
        var propertyInfos = destinationType.GetProperties();
        var fieldSet = false;
        foreach (var fieldInfo in propertyInfos)
        {

            if (!TryGetValue(data, fieldInfo, out var value))
            {
                continue;
            }

            if (value.GetType() == fieldInfo.PropertyType)
            {
                fieldInfo.SetValue(destination, value);
                fieldSet = true;
                continue;
            }
            var temp = value switch
            {
                string stringValue when fieldInfo.PropertyType.IsEnum => Enum.Parse(fieldInfo.PropertyType, stringValue),
                Dictionary<string, object?> objects => Map(fieldInfo.PropertyType, objects),
                List<Dictionary<string, object?>> collection => FooBar(fieldInfo, collection),
                _ => null,
            };

            if (temp is null)
            {
                Console.WriteLine($"Can't map {value} to type {fieldInfo.PropertyType} for field {fieldInfo.Name} on type {destinationType}");
                continue;
            }

            fieldInfo.SetValue(destination, temp);
            fieldSet = true;

        }
        return fieldSet ? destination : null;
    }
    private static bool TryGetValue(Dictionary<string, object?> data, PropertyInfo fieldInfo, [NotNullWhen(true)] out object? value)
    {
        value = null;
        // Only want field with [JsonPropertyName("...")]
        var customAttributeData = fieldInfo.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (customAttributeData == null)
        {
            return false;
        }

        // Try to get the value of the attribute from the Data dict
        var customAttributeTypedArgument = customAttributeData.Name;
        if (!data.TryGetValue(customAttributeTypedArgument, out value))
        {
            return false;
        }

        return value is not null;

    }
    private static void ParseString<T>(T destination, PropertyInfo? propertyInfo, string stringValue) where T : class
    {
        ArgumentNullException.ThrowIfNull(propertyInfo);
        propertyInfo.SetValue(destination, propertyInfo.PropertyType.IsEnum ? Enum.Parse(propertyInfo.PropertyType, stringValue) : stringValue);
    }

    // Amund: Old shi
    // Må caste alle object til rett type først. Kansje der er lurt å lage en egen type som kan takle dette istedet for "object" 
    // class Foo {
    // object Value;
    // TypeEnum ValueType / Enum, String, Object etc
    // }
    // Dette vil gjøre casting litt trygere. Enum verdien kan bli satt av ctor og ikke manuelt for å sikre at den stemmer med Value, en pattern matching av noe.
    // public static V1ServiceOwnerDialogsCommandsCreate_Dialog MapToCreateDialog(Dictionary<string, object> data)
    // {
    //
    //     var id = Guid.Empty;
    //     if (data["id"] is string)
    //     {
    //         id = Guid.Parse(data["id"].ToString() ?? string.Empty);
    //     }
    //
    //     return new V1ServiceOwnerDialogsCommandsCreate_Dialog
    //     {
    //         Id = id,
    //         IdempotentKey = GetString("idempotentKey", ref data) ?? string.Empty,
    //         ServiceResource = GetString("serviceResource", ref data) ?? string.Empty,
    //         Party = GetString("party", ref data) ?? string.Empty,
    //         Progress = GetInt("progress", ref data),
    //         ExtendedStatus = GetString("extendedStatus", ref data) ?? string.Empty,
    //         ExternalReference = GetString("externalReference", ref data) ?? string.Empty,
    //         VisibleFrom = null,
    //         DueAt = null,
    //         Process = GetString("process", ref data) ?? string.Empty,
    //         PrecedingProcess = GetString("precedingProcess", ref data) ?? string.Empty,
    //         ExpiresAt = null,
    //         CreatedAt = default,
    //         UpdatedAt = default,
    //         Status = (DialogsEntities_DialogStatus)(GetInt("status", ref data) ?? 0),
    //         SystemLabel = null,
    //         Content = null,
    //         SearchTags = null,
    //         Attachments = null,
    //         Transmissions = null,
    //         GuiActions = null,
    //         ApiActions = null,
    //         Activities = null
    //     };
    //
    // }
    //
    // private static string? GetString(string key, ref Dictionary<string, object> data)
    // {
    //     if (data[key] is string)
    //     {
    //         return data[key] as string;
    //     }
    //     return null;
    // }
    //
    // private static int? GetInt(string key, ref Dictionary<string, object> data)
    // {
    //     if (data[key] is int)
    //     {
    //         return (int)data[key];
    //     }
    //     return null;
    // }
}
