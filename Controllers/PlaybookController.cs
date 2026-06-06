using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("playbook")]
[EnableCors("AllowedOriginsPolicy")]
public class PlaybookController(
    IServiceownerApi dialogporten,
    IPlaybookStateStore stateStore,
    IOptions<ServiceProviderSettings> options,
    ILogger<PlaybookController> logger) : ControllerBase
{
    [Authorize]
    [Route("create")]
    [Consumes("application/json")]
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] CreatePlaybookRequest createPlaybookRequest, CancellationToken cancellationToken)
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

        if (createPlaybookRequest.FceContents is { } providedFceContents)
        {
            foreach (var (name, content) in providedFceContents)
            {
                if (!FceMediaTypes.IsAllowed(content.MediaType))
                {
                    return BadRequest($"FCE '{name}' has disallowed mediaType '{content.MediaType}'. Allowed: text/markdown, text/plain, text/html.");
                }
            }
        }

        var initialTitle = string.IsNullOrWhiteSpace(createPlaybookRequest.InitialTitle)
            ? "Playbook"
            : createPlaybookRequest.InitialTitle;
        var initialSummary = string.IsNullOrWhiteSpace(createPlaybookRequest.InitialSummary)
            ? "Playbook dialog"
            : createPlaybookRequest.InitialSummary;
        var initialLanguageCode = string.IsNullOrWhiteSpace(createPlaybookRequest.InitialLanguageCode)
            ? "en"
            : createPlaybookRequest.InitialLanguageCode;

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
                        new V1CommonLocalizations_Localization
                        {
                            Value = initialTitle,
                            LanguageCode = initialLanguageCode
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
                            Value = initialSummary,
                            LanguageCode = initialLanguageCode
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
        var dialogResult = await dialogporten.V1ServiceOwnerDialogsCommandsCreateDialog(dto, cancellationToken);
        if (!dialogResult.IsSuccessful)
        {
            logger.LogWarning(
                "Dialogporten POST /dialogs returned {StatusCode}. Body: {Body}",
                (int)dialogResult.StatusCode, dialogResult.Error?.Content);
            return BadRequest(dialogResult.Error?.Content);
        }

        if (!Guid.TryParse(dialogResult.Content!, out var dialogId))
        {
            return BadRequest("Parse Guid failed");
        }

        var blueprint = new PlaybookBlueprint(
            dialogId,
            playbookState.Patches,
            createPlaybookRequest.FceContents ?? new Dictionary<string, FceContent>());

        var stateId = await stateStore.CreateAsync(blueprint, cancellationToken);

        var compiler = new PlaybookCompiler(options.Value) { Progress = 0 };
        var compiledPatches = await compiler.CompilePatches(stateId, blueprint, playbookState.Cursor);
        if (compiledPatches.Count == 0)
        {
            return BadRequest("Cursor produced no patches.");
        }

        var patchResult = await dialogporten.V1ServiceOwnerDialogsPatchDialog(dialogId, compiledPatches, null, cancellationToken);
        if (!patchResult.IsSuccessful)
        {
            logger.LogWarning(
                "Dialogporten PATCH /dialogs/{DialogId} (bootstrap stage {Cursor}) returned {StatusCode}. Body: {Body}",
                dialogId, playbookState.Cursor, (int)patchResult.StatusCode, patchResult.Error?.Content);
            return BadRequest(patchResult.Error?.Content);
        }

        return Ok(new { dialogId, stateId });
    }
}

public class CreatePlaybookRequest
{
    public string Party { get; set; } = null!;
    public string ServiceResource { get; set; } = null!;
    public PlaybookState PlaybookState { get; set; } = null!;
    public Dictionary<string, FceContent>? FceContents { get; set; }
    public string? InitialTitle { get; set; }
    public string? InitialSummary { get; set; }
    public string? InitialLanguageCode { get; set; }
}
