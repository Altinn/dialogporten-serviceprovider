using System.Text.Json;
using System.Text.Json.Nodes;
using Altinn.ApiClients.Dialogporten.Features.V1;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public class PlaybookCompiler(ServiceProviderSettings settings)
{
    private readonly string _baseUri = settings.MutateBaseUri.TrimEnd('/');
    private const int MaxDepth = 32;
    private const int MaxNodes = 1000;
    private int _visitedNodes;

    public int Progress { get; set; } = 0;

    public Task<List<JsonPatchOperations_Operation>> CompilePatches(string stateId, PlaybookBlueprint blueprint, int cursor)
    {
        _visitedNodes = 0;
        if (cursor < 0 || cursor >= blueprint.Patches.Count)
        {
            return Task.FromResult<List<JsonPatchOperations_Operation>>([]);
        }

        var stagePatches = blueprint.Patches[cursor]?.Deserialize<List<JsonPatchOperations_Operation>>();
        return CompileStageAsync(stagePatches, stateId, cursor);
    }

    private async Task<List<JsonPatchOperations_Operation>> CompileStageAsync(List<JsonPatchOperations_Operation>? stagePatches, string stateId, int cursor)
    {
        List<JsonPatchOperations_Operation> compiled = [];
        if (stagePatches == null) return compiled;

        foreach (var patch in stagePatches)
        {
            var rewritten = await CompilePatch(patch, stateId, cursor);
            compiled.Add(rewritten ?? patch);
        }
        return compiled;
    }

    private async Task<JsonPatchOperations_Operation?> CompilePatch(JsonPatchOperations_Operation patch, string stateId, int cursor)
    {
        switch (patch.Value)
        {
            case JsonElement { ValueKind: JsonValueKind.String } stringValue:
                {
                    var raw = stringValue.GetString();
                    if (Lexer.TryParseCommand(raw, out var command))
                    {
                        return CreateUpdatedPatch(patch, command, stateId, cursor);
                    }
                    if (raw != null && ContainsPlaceholder(raw))
                    {
                        return new JsonPatchOperations_Operation
                        {
                            OperationType = patch.OperationType,
                            Path = patch.Path,
                            Op = patch.Op,
                            From = patch.From,
                            Value = JsonValue.Create(SubstitutePlaceholders(raw, stateId))
                        };
                    }
                    break;
                }
            case JsonElement { ValueKind: JsonValueKind.Object or JsonValueKind.Array } objectValue:
                {
                    var updated = await ProcessJsonElement(objectValue, stateId, cursor, 0);
                    if (updated.HasValue)
                    {
                        return new JsonPatchOperations_Operation
                        {
                            OperationType = patch.OperationType,
                            Path = patch.Path,
                            Op = patch.Op,
                            From = patch.From,
                            Value = updated.Value
                        };
                    }
                    break;
                }
        }
        return null;
    }

    private int ComputeNextCursor(Command command, int currentCursor)
    {
        return command.Type switch
        {
            CommandType.Next => currentCursor + 1,
            CommandType.Previous => currentCursor - 1,
            CommandType.Goto => (int)command.Value,
            CommandType.GotoIfProgress => ResolveGotoIfProgress((GotoIfProgressValue)command.Value),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private int ResolveGotoIfProgress(GotoIfProgressValue value) =>
        Progress == value.Progress ? value.Goto : value.Else;

    private string BuildMutateUrl(string stateId, int cursor) =>
        $"{_baseUri}/mutate/{stateId}/{cursor}";

    private static bool ContainsPlaceholder(string raw) =>
        raw.Contains("{baseUri}") || raw.Contains("{stateId}");

    private string SubstitutePlaceholders(string raw, string stateId) =>
        raw.Replace("{baseUri}", _baseUri).Replace("{stateId}", stateId);

    private bool ExceedsLimits(int depth)
    {
        if (depth > MaxDepth)
        {
            return true;
        }

        _visitedNodes++;
        return _visitedNodes > MaxNodes;
    }

    private async Task<JsonElement?> ProcessJsonElement(JsonElement element, string stateId, int cursor, int depth)
    {
        if (ExceedsLimits(depth))
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var stringValue = element.GetString();
                if (Lexer.TryParseCommand(stringValue, out var command))
                {
                    var nextCursor = ComputeNextCursor(command, cursor);
                    return JsonSerializer.SerializeToElement(BuildMutateUrl(stateId, nextCursor));
                }
                if (stringValue != null && ContainsPlaceholder(stringValue))
                {
                    return JsonSerializer.SerializeToElement(SubstitutePlaceholders(stringValue, stateId));
                }
                return null;

            case JsonValueKind.Object:
                return await ProcessObject(element, stateId, cursor, depth + 1);

            case JsonValueKind.Array:
                return await ProcessArray(element, stateId, cursor, depth + 1);

            case JsonValueKind.Undefined:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            default:
                return null;
        }
    }

    private async Task<JsonElement?> ProcessObject(JsonElement objectValue, string stateId, int cursor, int depth)
    {
        var updates = new Dictionary<string, JsonElement>();
        var hasChanges = false;

        foreach (var property in objectValue.EnumerateObject())
        {
            var value = property.Value;
            var updated = await ProcessJsonElement(property.Value, stateId, cursor, depth);
            if (updated.HasValue)
            {
                value = updated.Value;
                hasChanges = true;
            }

            updates[property.Name] = value;
        }

        if (!hasChanges)
        {
            return null;
        }

        using var stream = new MemoryStream();
        await using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        foreach (var kvp in updates)
        {
            writer.WritePropertyName(kvp.Key);
            kvp.Value.WriteTo(writer);
        }
        writer.WriteEndObject();
        await writer.FlushAsync();

        var jsonBytes = stream.ToArray();
        return JsonDocument.Parse(jsonBytes).RootElement;
    }

    private async Task<JsonElement?> ProcessArray(JsonElement arrayValue, string stateId, int cursor, int depth)
    {
        var updates = new List<JsonElement>();
        var hasChanges = false;

        foreach (var item in arrayValue.EnumerateArray())
        {
            var value = item;
            var updated = await ProcessJsonElement(item, stateId, cursor, depth);
            if (updated.HasValue)
            {
                value = updated.Value;
                hasChanges = true;
            }

            updates.Add(value);
        }

        if (!hasChanges)
        {
            return null;
        }

        using var stream = new MemoryStream();
        await using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartArray();
        foreach (var element in updates)
        {
            element.WriteTo(writer);
        }
        writer.WriteEndArray();
        await writer.FlushAsync();

        var jsonBytes = stream.ToArray();
        return JsonDocument.Parse(jsonBytes).RootElement;
    }

    private JsonPatchOperations_Operation CreateUpdatedPatch(JsonPatchOperations_Operation patch, Command command, string stateId, int cursor)
    {
        var nextCursor = ComputeNextCursor(command, cursor);
        return new JsonPatchOperations_Operation
        {
            OperationType = patch.OperationType,
            Path = patch.Path,
            Op = patch.Op,
            From = patch.From,
            Value = JsonValue.Create(BuildMutateUrl(stateId, nextCursor))
        };
    }
}
