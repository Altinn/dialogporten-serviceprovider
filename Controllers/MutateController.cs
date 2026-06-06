using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[Authorize(AuthenticationSchemes = "DialogToken")]
[ApiController]
[Route("mutate")]
[EnableCors("AllowedOriginsPolicy")]
public class MutateController(
    IServiceownerApi dialogporten,
    IPlaybookStateStore stateStore,
    IOptions<ServiceProviderSettings> options,
    ILogger<MutateController> logger) : ControllerBase
{
    [HttpPost]
    [Route("{stateId}/{cursor:int}")]
    public async Task<IActionResult> MutatePlaybook(
        [FromRoute] string stateId,
        [FromRoute] int cursor,
        CancellationToken cancellationToken)
    {
        var blueprint = await stateStore.GetAsync(stateId, cancellationToken);
        if (blueprint is null)
        {
            return NotFound();
        }

        var tokenDialogIdRaw = User.FindFirst("i")?.Value;
        if (!Guid.TryParse(tokenDialogIdRaw, out var tokenDialogId) || tokenDialogId != blueprint.DialogId)
        {
            return Forbid();
        }

        if (cursor < 0 || cursor >= blueprint.Patches.Count)
        {
            return NotFound();
        }

        var compiler = new PlaybookCompiler(options.Value)
        {
            Progress = 0
        };

        var dialogResponse = await dialogporten.V1ServiceOwnerDialogsQueriesGetDialog(blueprint.DialogId, null!, cancellationToken);
        if (!dialogResponse.IsSuccessful)
        {
            logger.LogWarning(
                "Dialogporten GET /dialogs/{DialogId} returned {StatusCode}. Body: {Body}",
                blueprint.DialogId, (int)dialogResponse.StatusCode, dialogResponse.Error?.Content);
            return BadRequest();
        }

        compiler.Progress = dialogResponse.Content!.Progress ?? 0;

        var patches = await compiler.CompilePatches(stateId, blueprint, cursor);
        if (patches.Count == 0)
        {
            return BadRequest();
        }

        var patchResult = await dialogporten.V1ServiceOwnerDialogsPatchDialog(blueprint.DialogId, patches, null, cancellationToken);
        if (!patchResult.IsSuccessful)
        {
            logger.LogWarning(
                "Dialogporten PATCH /dialogs/{DialogId} for stateId={StateId} cursor={Cursor} returned {StatusCode}. Body: {Body}",
                blueprint.DialogId, stateId, cursor, (int)patchResult.StatusCode, patchResult.Error?.Content);
            return BadRequest(patchResult.Error?.Content);
        }

        return Ok();
    }
}
