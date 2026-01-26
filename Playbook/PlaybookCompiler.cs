using System.Text.Json;
using System.Text.Json.Nodes;
using Altinn.ApiClients.Dialogporten.Features.V1;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public class PlaybookCompiler(ServiceProviderSettings settings)
{
    private const string Endpoint = "mutate/";
    private readonly string _path = settings.MutateBaseUri + Endpoint;
    private const int MaxDepth = 32;
    private const int MaxNodes = 1000;
    private int _visitedNodes;

    public int Progress { get; set; } = 0;

    public async Task<List<JsonPatchOperations_Operation>> CompilePatches(PlaybookState playbookState)
    {
        _visitedNodes = 0;
        var internalPlayBookState = new PlaybookState
        (
            playbookState.DialogId,
            playbookState.Cursor,
            playbookState.Patches
        );
        var patches = internalPlayBookState.CurrentPatch();
        List<JsonPatchOperations_Operation> compiledPatches = [];
        if (patches == null)
        {
            return compiledPatches;
        }
        foreach (var patch in patches.ToList())
        {
            var compiledPatch = await CompilePatch(patch, internalPlayBookState);
            compiledPatches.Add(compiledPatch ?? patch);
        }
        return compiledPatches;
    }

    private async Task<JsonPatchOperations_Operation?> CompilePatch(JsonPatchOperations_Operation patch, PlaybookState playbookState)
    {
        switch (patch.Value)
        {
            case JsonElement { ValueKind: JsonValueKind.String } stringValue:
                {
                    if (Lexer.TryParseCommand(stringValue.GetString(), out var command))
                    {
                        return await CreateUpdatedPatch(patch, command, playbookState);
                    }
                    break;
                }
            case JsonElement { ValueKind: JsonValueKind.Object or JsonValueKind.Array } objectValue:
                {
                    var updated = await ProcessJsonElement(objectValue, playbookState, 0);
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

    private Task<string> UpdateAndEncode(PlaybookState playbookState, Command command)
    {
        switch (command.Type)
        {
            case CommandType.Next:
                playbookState.Cursor += 1;
                break;
            case CommandType.Previous:
                playbookState.Cursor -= 1;
                break;
            case CommandType.Goto:
                playbookState.Cursor = (int)command.Value;
                break;
            case CommandType.GotoIfProgress:
                // POC complex Logic
                var aa = (GotoIfProgressValue)command.Value;
                playbookState.Cursor = Progress == aa.Progress ? aa.Goto : aa.Else;

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        return playbookState.EncodeToBase64();
    }
    private bool ExceedsLimits(int depth)
    {
        if (depth > MaxDepth)
        {
            return true;
        }

        _visitedNodes++;
        return _visitedNodes > MaxNodes;
    }

    private async Task<JsonElement?> ProcessJsonElement(JsonElement element, PlaybookState playbookState, int depth)
    {
        if (ExceedsLimits(depth))
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (Lexer.TryParseCommand(element.GetString(), out var command))
                {
                    var compiledPlaybook = await UpdateAndEncode(playbookState, command);
                    return JsonSerializer.SerializeToElement(_path + compiledPlaybook);
                }
                return null;

            case JsonValueKind.Object:
                return await ProcessObject(element, playbookState, depth + 1);

            case JsonValueKind.Array:
                return await ProcessArray(element, playbookState, depth + 1);

            case JsonValueKind.Undefined:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            default:
                return null;
        }

    }

    private async Task<JsonElement?> ProcessObject(JsonElement objectValue, PlaybookState playbookState, int depth)
    {
        var updates = new Dictionary<string, JsonElement>();
        var hasChanges = false;

        foreach (var property in objectValue.EnumerateObject())
        {
            var value = property.Value;
            var updated = await ProcessJsonElement(property.Value, playbookState, depth);
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

    private async Task<JsonElement?> ProcessArray(JsonElement arrayValue, PlaybookState playbookState, int depth)
    {
        var updates = new List<JsonElement>();
        var hasChanges = false;

        foreach (var item in arrayValue.EnumerateArray())
        {
            var value = item;
            var updated = await ProcessJsonElement(item, playbookState, depth);
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
    private async Task<JsonPatchOperations_Operation?> CreateUpdatedPatch(JsonPatchOperations_Operation patch, Command command, PlaybookState playbookState)
    {
        var base64 = await UpdateAndEncode(playbookState, command);

        return new JsonPatchOperations_Operation
        {
            OperationType = patch.OperationType,
            Path = patch.Path,
            Op = patch.Op,
            From = patch.From,
            Value = JsonValue.Create(_path + base64)
        };
    }
}
