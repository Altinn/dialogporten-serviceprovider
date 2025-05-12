using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Digdir.BDB.Dialogporten.ServiceProvider;

public static class FieldParser
{
    public static IEnumerable<FieldRecord?> ParseFields(JsonElement jsonElement)
    {
        return jsonElement.EnumerateObject()
                          .Select<JsonProperty, FieldRecord?>(jsonProperty => ParseField(jsonProperty))
                          .ToList();
    }
    private static FieldRecord? ParseField(JsonProperty jsonProperty)
    {
        var propertyName = jsonProperty.Name;
        var jsonElement = jsonProperty.Value;

        if (!jsonElement.TryGetTypes(out var isNullable, out var fieldType, out var enumValues))
        {
            return null;
        }

        jsonElement.TryGetFormat(out var format);
        var desc = "";
        if (jsonElement.TryGetProperty("description", out var descElement))
        {
            desc = descElement.GetString()!;
        }

        switch (fieldType)
        {
            case FieldTypes.Enum when enumValues != null:
                return new FieldRecord.EnumRecord(propertyName, desc, isNullable, enumValues);
            case FieldTypes.String:
                return format switch
                {
                    "date-time" => new FieldRecord.DateTimeRecord(propertyName, desc, isNullable),
                    "guid" => new FieldRecord.GuidRecord(propertyName, desc, isNullable),
                    _ => new FieldRecord.StringRecord(propertyName, desc, isNullable, format)
                };
            case FieldTypes.Integer:
                {
                    var min = jsonElement.GetProperty("minimum").GetInt32();
                    var max = jsonElement.GetProperty("maximum").GetInt32();
                    return new FieldRecord.IntRecord(propertyName, desc, isNullable, min, max, format);
                }
            case FieldTypes.Array:
                {
                    var itemFormat = ParseFields(jsonElement.GetProperty("items").GetProperty("properties")).ToArray();
                    return new FieldRecord.ArrayRecord(propertyName, desc, itemFormat, isNullable);
                }
            case FieldTypes.Object:
                return new FieldRecord.ObjectRecord(propertyName, ParseFields(jsonElement.GetProperty("properties")), isNullable);
            case FieldTypes.TextArea:
                return new FieldRecord.TextAreaRecord(propertyName, desc, isNullable);
            case FieldTypes.None:
                break;
        }
        throw new UnreachableException();
    }
    private static bool TryGetTypes(
        this JsonElement jsonElement,
        out bool isNullable,
        out FieldTypes fieldType,
        out string[]? enumValues)
    {
        fieldType = FieldTypes.None;
        isNullable = false;
        enumValues = null;

        if (jsonElement.ValueKind == JsonValueKind.String)
        {
            fieldType = FieldTypes.None;
            isNullable = false;
            return fieldType != FieldTypes.None;
        }
        if (!jsonElement.TryGetProperty("type", out var type))
        {
            fieldType = FieldTypes.TextArea;
            isNullable = true;
            return true;
        }

        if (jsonElement.TryGetProperty("enum", out var values))
        {
            enumValues = values.EnumerateArray()
                               .Select(x => x.GetString()!)
                               .ToArray();
            fieldType = FieldTypes.Enum;
            return true;
        }

        string[] types;
        if (type.ValueKind == JsonValueKind.Array)
        {
            types = type.EnumerateArray()
                        .Where(element =>
                            element.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!).ToArray();

            if (types.Contains("null"))
            {
                isNullable = true;
            }

            if (Enum.TryParse(types.First(x => x != "null"), true, out fieldType))
            {
                return fieldType != FieldTypes.None;
            }
        }
        else
        {
            types = [type.GetString()!];
            isNullable = false;

            if (Enum.TryParse(types.First(), true, out fieldType))
            {
                return fieldType != FieldTypes.None;
            }
        }

        return fieldType != FieldTypes.None;
    }

    private static bool TryGetFormat(this JsonElement jsonElement, [NotNullWhen(true)] out string? format)
    {
        format = null;
        if (jsonElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!jsonElement.TryGetProperty("format", out var formatElement))
        {
            return false;
        }

        if (formatElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        format = formatElement.GetString()!;
        return true;
    }


    private enum FieldTypes
    {
        None,
        String,
        Integer,
        Enum,
        Object,
        Array,
        TextArea
    }
}
