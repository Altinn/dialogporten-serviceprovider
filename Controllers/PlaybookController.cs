using System.Text;
using System.Text.Json;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("playbook")]
[EnableCors("AllowedOriginsPolicy")]
public class PlaybookController : ControllerBase
{

    [Route("create")]
    [Consumes("application/json")]
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] JsonElement jsonBody)
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

        return Content(encoded, "text/plain", Encoding.UTF8);
    }
}
