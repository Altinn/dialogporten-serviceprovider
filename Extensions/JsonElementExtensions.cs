using System.Text.Json;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Microsoft.IdentityModel.Tokens;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Extensions;

public static class JsonElementExtensions
{
    public static async Task<string> Encode(this JsonElement jsonBody)
    {

        using var stream = new MemoryStream();
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false
        });
        jsonBody.WriteTo(writer);
        await writer.FlushAsync();

        var bytes = stream.ToArray();

        var compressed = await CompressionExtensions.CompressBytesAsync(bytes);
        var encoded = Base64UrlEncoder.Encode(compressed);
        return encoded;
    }
}
