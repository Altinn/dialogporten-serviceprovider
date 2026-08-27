using Digdir.BDB.Dialogporten.ServiceProvider.Extensions;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("fce/named")]
[Authorize(AuthenticationSchemes = "DialogToken")]
[EnableCors("AllowedOriginsPolicy")]
public class NamedFrontChannelEmbedController(IPlaybookStateStore stateStore) : ControllerBase
{
    [HttpGet]
    [Route("{stateId:guid}/{fceName}")]
    public async Task<IActionResult> Get(
        [FromRoute] string stateId,
        [FromRoute] string fceName,
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

        if (!blueprint.FceContents.TryGetValue(fceName, out var content))
        {
            return NotFound();
        }

        if (!FceMediaTypes.IsAllowed(content.MediaType))
        {
            return NotFound();
        }

        // Hackathon setting: the embed body is served with a wide-open policy so arbitrary HTML
        // renders in Arbeidsflate — images (remote and data: URIs), remote stylesheets and fonts,
        // inline styles, inline scripts, nested iframes and outbound fetches are all permitted, and
        // no `sandbox` directive is sent. That means an FCE body is trusted code running with the
        // dialog's context; tighten this back to
        // `sandbox; default-src 'none'; style-src 'unsafe-inline'` before anything but a demo.
        Response.Headers["Content-Security-Policy"] =
            "default-src * data: blob: filesystem: 'unsafe-inline' 'unsafe-eval'; " +
            "script-src * data: blob: 'unsafe-inline' 'unsafe-eval'; " +
            "style-src * data: blob: 'unsafe-inline'; " +
            "img-src * data: blob:; " +
            "media-src * data: blob:; " +
            "font-src * data: blob:; " +
            "connect-src * data: blob: ws: wss:; " +
            "frame-src * data: blob:; " +
            "child-src * data: blob:; " +
            "form-action *; " +
            "frame-ancestors *";

        // Phase D/E: render {if:...} conditional blocks and interpolate {vars.X} in the FCE body
        // using current session vars, so embed content reflects live state when Arbeidsflate
        // loads the iframe.
        var sessionVars = await stateStore.GetSessionVarsAsync(stateId, cancellationToken);

        // {baseUri}/{stateId}/{cursor:STAGE}/{formAction:STAGE} first, so an embedded form gets a
        // real URL to post back to, then {vars.X} and {if:} against live session state.
        var callbackBaseUri = string.IsNullOrWhiteSpace(blueprint.MutateBaseUri)
            ? this.AppBaseUri()
            : blueprint.MutateBaseUri;
        var body = EmbedPlaceholders.Render(content.Content, callbackBaseUri, stateId, blueprint.StageCursors);
        body = Evaluator.RenderTemplate(body, sessionVars);
        return Content(body, content.MediaType);
    }
}
