using System.Text.Json;
using System.Text.Json.Nodes;
using Altinn.ApiClients.Dialogporten.Features.V1;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public class PlaybookCompiler
{

    private const string Path = "https://localhost:7247/mutate/";

    public int Progress { get; set; }

    public async Task<List<JsonPatchOperations_Operation>> CompilePatches(PlaybookState playbookState)
    {
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

    public async Task<V1ServiceOwnerDialogsCommandsCreate_GuiAction> CompileGuiAction(V1ServiceOwnerDialogsCommandsCreate_GuiAction guiAction, PlaybookState playbookState)
    {
        var gui = new V1ServiceOwnerDialogsCommandsCreate_GuiAction
        {
            Id = guiAction.Id,
            Action = guiAction.Action,
            AuthorizationAttribute = guiAction.AuthorizationAttribute,
            IsDeleteDialogAction = guiAction.IsDeleteDialogAction,
            HttpMethod = guiAction.HttpMethod,
            Priority = guiAction.Priority,
            Title = guiAction.Title,
            Prompt = guiAction.Prompt
        };

        if (Lexer.TryParseCommand(guiAction.Url.ToString(), out var command))
        {
            gui.Url = new Uri(Path + await UpdateAndEncode(playbookState, command));
            return gui;
        }

        gui.Url = guiAction.Url;
        return gui;

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
            case JsonElement { ValueKind: JsonValueKind.Object } objectValue:
                {
                    var updatedObject = await ProcessJsonObject(objectValue, playbookState);
                    if (updatedObject != null)
                    {
                        return new JsonPatchOperations_Operation
                        {
                            OperationType = patch.OperationType,
                            Path = patch.Path,
                            Op = patch.Op,
                            From = patch.From,
                            Value = updatedObject
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
    private async Task<JsonElement?> ProcessJsonObject(JsonElement objectValue, PlaybookState playbookState)
    {
        var updates = new Dictionary<string, JsonElement>();
        var hasChanges = false;

        foreach (var property in objectValue.EnumerateObject())
        {
            var value = property.Value;
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    {
                        if (Lexer.TryParseCommand(property.Value.GetString(), out var command))
                        {
                            var compiledPlaybook = await UpdateAndEncode(playbookState, command);
                            value = JsonSerializer.SerializeToElement(Path + compiledPlaybook);
                            hasChanges = true;
                        }
                        updates[property.Name] = value;
                        break;
                    }
                case JsonValueKind.Object:
                    {
                        var nestedResult = await ProcessJsonObject(property.Value, playbookState);
                        if (nestedResult.HasValue)
                        {
                            value = nestedResult.Value;
                            hasChanges = true;
                        }

                        updates[property.Name] = value;
                        break;
                    }
                case JsonValueKind.Undefined:
                case JsonValueKind.Array:
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                default:
                    updates[property.Name] = property.Value;
                    break;
            }
        }
        if (!hasChanges)
        {
            return null;
        }

        // Reconstruct the JSON object with updates
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
    private async Task<JsonPatchOperations_Operation?> CreateUpdatedPatch(JsonPatchOperations_Operation patch, Command command, PlaybookState playbookState)
    {
        var base64 = await UpdateAndEncode(playbookState, command);

        return new JsonPatchOperations_Operation
        {
            OperationType = patch.OperationType,
            Path = patch.Path,
            Op = patch.Op,
            From = patch.From,
            Value = JsonValue.Create(Path + base64)
        };
    }
}
