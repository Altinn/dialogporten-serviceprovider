using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Altinn.ApiClients.Dialogporten.Features.V1;
using Microsoft.IdentityModel.Tokens;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public class PlaybookState(Guid dialogId, int cursor, JsonArray patches)
{
    public Guid DialogId { get; set; } = dialogId;

    public JsonArray Patches { get; set; } = patches;

    public int Cursor { get; set; } = cursor;

    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = false };

    public List<JsonPatchOperations_Operation>? CurrentPatch()
    {
        if (Cursor >= Patches.Count || Cursor < 0) return null;
        var jsonPatch = Patches[Cursor];
        return jsonPatch.Deserialize<List<JsonPatchOperations_Operation>>();
    }

    public static async Task<PlaybookState?> DecodeFromBase64(string base64)
    {
        var decoded = Base64UrlEncoder.DecodeBytes(base64);
        var decompressed = await CompressionExtensions.DecompressBytesAsync(decoded!);

        return JsonSerializer.Deserialize<PlaybookState>(Encoding.UTF8.GetString(decompressed));
    }

    public static Task<string> EncodeToBase64(PlaybookState playbookState)
    {
        return playbookState.EncodeToBase64();
    }

    public async Task<string> EncodeToBase64()
    {
        var json = JsonSerializer.Serialize(this, JsonSerializerOptions);
        var compressed = await CompressionExtensions.CompressBytesAsync(Encoding.UTF8.GetBytes(json));

        return Base64UrlEncoder.Encode(compressed);
    }
}
