using System.Text;
using System.Text.Json;
using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Extensions;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("playbook")]
[EnableCors("AllowedOriginsPolicy")]
public class PlaybookController(IServiceownerApi dialogporten) : ControllerBase
{

    [Route("encode")]
    [Consumes("application/json")]
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] JsonElement jsonBody)
    {
        return Content(await jsonBody.Encode(), "text/plain", Encoding.UTF8);
    }


    [Authorize]
    [Route("create")]
    [Consumes("application/json")]
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] CreatePlaybookRequest createPlaybookRequest)
    {
        var playbookState = createPlaybookRequest.PlaybookState;

        if (playbookState.Patches.Count == 0)
        {
            return BadRequest("Need at least 1 Patch");
        }
        if (playbookState.Cursor < 0 || playbookState.Cursor >= playbookState.Patches.Count)
        {
            return BadRequest("Cursor is out of range");
        }

        var dto = new V1ServiceOwnerDialogsCommandsCreate_Dialog
        {
            ServiceResource = createPlaybookRequest.ServiceResource,
            Party = createPlaybookRequest.Party,
            Content = new V1ServiceOwnerDialogsCommandsCreate_Content
            {
                Title = new V1CommonContent_ContentValue
                {
                    Value =
                    [
                        new()
                        {
                            Value = "Første tittel",
                            LanguageCode = "nb"
                        }
                    ],
                    MediaType = "text/plain"
                },
                Summary = new V1CommonContent_ContentValue
                {
                    Value =
                    [
                        new V1CommonLocalizations_Localization
                        {
                            Value = "Første Summary",
                            LanguageCode = "nb"
                        }
                    ],
                    MediaType = "text/plain"
                },
            },
            SearchTags =
            [
                new V1ServiceOwnerDialogsCommandsCreate_Tag
                {
                    Value = "Playbook"
                }
            ],
        };
        var dialogResult = await dialogporten.V1ServiceOwnerDialogsCommandsCreateDialog(dto, CancellationToken.None);
        if (!dialogResult.IsSuccessful)
        {
            return BadRequest(dialogResult.Error.Content);
        }

        if (!Guid.TryParse(dialogResult.Content!, out var guid))
        {
            return BadRequest("Parse Guid failed");
        }

        playbookState.DialogId = guid;


        return new OkResult();

    }
}

public class CreatePlaybookRequest
{
    public string Party { get; set; }
    public string ServiceResource { get; set; }
    public PlaybookState PlaybookState { get; set; }

}
