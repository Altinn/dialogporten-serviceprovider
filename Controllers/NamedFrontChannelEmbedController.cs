using Digdir.BDB.Dialogporten.ServiceProvider.Extensions;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;
using Digdir.BDB.Dialogporten.ServiceProvider.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("fce/named")]
[Authorize(AuthenticationSchemes = "DialogToken")]
[EnableCors("AllowedOriginsPolicy")]
public class NamedFrontChannelEmbedController(
    IPlaybookStateStore stateStore,
    IDialogportenEnvironmentRegistry environments,
    IOptions<ServiceProviderSettings> options) : ControllerBase
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

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; style-src 'unsafe-inline'";

        // Phase D/E: render {if:...} conditional blocks and interpolate {vars.X} in the FCE body
        // using current session vars, so embed content reflects live state when Arbeidsflate
        // loads the iframe.
        var sessionVars = await stateStore.GetSessionVarsAsync(stateId, cancellationToken);

        // Mirrors PlaybookCompiler.SubstitutePlaceholders, which only runs on patch values.
        var baseUri = string.IsNullOrWhiteSpace(blueprint.MutateBaseUri)
            ? environments.TryResolve(blueprint.EnvironmentKey, out var environment)
                ? environment.ResolveCallbackBaseUri(options.Value.MutateBaseUri, this.AppBaseUri())
                : this.AppBaseUri()
            : blueprint.MutateBaseUri;
        var body = Evaluator.RenderTemplate(
            content.Content.Replace("{baseUri}", baseUri).Replace("{stateId}", stateId),
            sessionVars);
        return Content(body, content.MediaType);
    }
}
