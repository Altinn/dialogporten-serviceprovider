using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;



namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("mutate")]
[Authorize]
[EnableCors("AllowedOriginsPolicy")]
public class MutateController(IServiceownerApi dialogporten) : ControllerBase
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


}
