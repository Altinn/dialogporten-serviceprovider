using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;


namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("mutate")]
[Authorize]
[EnableCors("AllowedOriginsPolicy")]
public class MutateController(IServiceownerApi dialogporten) : ControllerBase
{

    private const string Path = "https://localhost:7247/mutate/";
    [HttpGet]
    [Route("{base64PlaybookState}")]
    public async Task<IActionResult> MutatePlaybook(
        [FromRoute] string base64PlaybookState)
    {

        var playbookState = await PlaybookState.DecodeFromBase64(base64PlaybookState);
        if (playbookState == null)
        {
            return new BadRequestResult();
        }


        var patches = await CompilePatchesAndUpdateState(playbookState.CurrentPatch(), playbookState);
        if (patches.Count == 0)
        {
            return new BadRequestResult();
        }


        var patchResult = await dialogporten.V1ServiceOwnerDialogsPatchDialog(playbookState.DialogId, patches, null, CancellationToken.None);

        if (!patchResult.IsSuccessful)
        {
            Console.WriteLine(patchResult.Error);
        }

        return new OkResult();
    }

    private static async Task<List<JsonPatchOperations_Operation>> CompilePatchesAndUpdateState(List<JsonPatchOperations_Operation>? patches, PlaybookState playbookState)
    {
        List<JsonPatchOperations_Operation> compiledPatches = [];
        if (patches == null)
        {
            return compiledPatches;
        }
        foreach (var patch in patches.ToList())
        {
            var compiledPatch = await CompilePatch(patch, playbookState);
            if (compiledPatch == null)
            {
                compiledPatches.Add(patch);
            }
            else
            {
                compiledPatches.Add(compiledPatch);
            }
        }
        return compiledPatches;
    }

    private static async Task<JsonPatchOperations_Operation?> CompilePatch(JsonPatchOperations_Operation patch, PlaybookState playbookState)
    {
        switch (patch.Value)
        {
            case JsonElement { ValueKind: JsonValueKind.String } stringValue:
                {
                    var command = Lexer.ParseCommand(stringValue.GetString()!);
                    if (command != null)
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
    private static async Task<JsonElement?> ProcessJsonObject(JsonElement objectValue, PlaybookState playbookState)
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
                        var command = Lexer.ParseCommand(property.Value.GetString()!);
                        if (command != null)
                        {
                            var compiledPlaybook = await playbookState.Compile(command);
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
    private static async Task<JsonPatchOperations_Operation?> CreateUpdatedPatch(JsonPatchOperations_Operation patch, Command command, PlaybookState playbookState)
    {
        var base64 = await playbookState.Compile(command);

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
