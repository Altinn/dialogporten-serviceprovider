using System.Diagnostics.CodeAnalysis;
using Altinn.ApiClients.Dialogporten;
using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;


namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("mutate")]
[EnableCors("AllowedOriginsPolicy")]
public class MutateController(IServiceownerApi dialogporten, IDialogTokenValidator dialogTokenValidator) : ControllerBase
{

    [HttpGet]
    [Route("{base64PlaybookState}")]
    public async Task<IActionResult> MutatePlaybook(
        [FromRoute] string base64PlaybookState)
    {
        var compiler = new PlaybookCompiler
        {
            Progress = 0
        };
        var playbookState = await PlaybookState.DecodeFromBase64(base64PlaybookState);

        if (playbookState == null)
        {
            return new BadRequestResult();
        }

        if (playbookState.DialogId == Guid.Empty)
        {
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (!TryGetGuidFromToken(authHeader, out var guid))
            {
                return new BadRequestResult();
            }

            playbookState.DialogId = guid.Value;
        }


        var dialog = await dialogporten.V1ServiceOwnerDialogsQueriesGetDialog(playbookState.DialogId, null!, CancellationToken.None);
        if (!dialog.IsSuccessful)
        {
            return new BadRequestResult();
        }

        compiler.Progress = dialog.Content!.Progress!.Value;

        var patches = await compiler.CompilePatches(playbookState);
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


    private bool TryGetGuidFromToken(string? token, [NotNullWhen(true)] out Guid? guid)
    {
        guid = null;

        if (token == null || !token.StartsWith("Bearer "))
        {
            return false;
        }

        var jwtToken = token["Bearer ".Length..].Trim();
        var result = dialogTokenValidator.Validate(jwtToken);

        if (!result.IsValid)
        {
            return false;
        }

        var dialogIdClaim = result.ClaimsPrincipal.Claims.FirstOrDefault(c => c.Type == "i");
        if (!Guid.TryParse(dialogIdClaim?.Value, out var dialogId))
        {
            return false;
        }

        guid = dialogId;
        return true;

    }
}
