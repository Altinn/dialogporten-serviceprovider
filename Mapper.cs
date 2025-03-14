using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Digdir.BDB.Dialogporten.ServiceProvider;

public static class Mapper
{
    public static T? Map<T>(Dictionary<string, object?> data) where T : class, new()
    {
        var destination = new T();
        var destinationType = typeof(T);
        var propertyInfos = destinationType.GetProperties();
        var fieldSet = false;
        foreach (var fieldInfo in propertyInfos)
        {
            if (!TryGetValue(data, fieldInfo, out var value))
            {
                continue;
            }

            switch (value)
            {
                case string stringValue:
                    ParseString(destination, fieldInfo, stringValue);
                    Console.WriteLine($"{fieldInfo.Name} : {stringValue}");
                    fieldSet = true;
                    break;
                case int intValue:
                    fieldInfo.SetValue(destination, intValue);
                    Console.WriteLine($"{fieldInfo.Name} : {intValue}");
                    fieldSet = true;
                    break;
                case Guid guidValue:
                    fieldInfo.SetValue(destination, guidValue);
                    Console.WriteLine($"{fieldInfo.Name} : {guidValue}");
                    fieldSet = true;
                    break;
                default:
                    Console.WriteLine($"{fieldInfo.Name} type({value?.GetType()}) : {value}");
                    break;
            }
        }
        return fieldSet ? destination : null;
    }
    private static bool TryGetValue(Dictionary<string, object?> data, PropertyInfo fieldInfo, [NotNullWhen(true)] out object? value)
    {
        value = null;
        // Only want field with [JsonPropertyName("...")]
        var customAttributeData = fieldInfo.CustomAttributes.FirstOrDefault();
        if (customAttributeData == null)
        {
            return false;
        }

        // Check if attribute is correct
        var attributeType = customAttributeData.AttributeType;
        if (attributeType != typeof(JsonPropertyNameAttribute))
        {
            return false;
        }

        // Try to get the value of the attribute from the Data dict
        var customAttributeTypedArgument = customAttributeData.ConstructorArguments.FirstOrDefault().Value as string;
        if (!data.TryGetValue(customAttributeTypedArgument!.Trim('"'), out value))
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
