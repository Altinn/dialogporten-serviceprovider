using System.IO;
using System.Text.Json.Nodes;
using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;
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

        return await BootstrapPlaybookAsync(
            createPlaybookRequest.Party,
            createPlaybookRequest.ServiceResource,
            createPlaybookRequest.InitialTitle,
            createPlaybookRequest.InitialSummary,
            createPlaybookRequest.InitialLanguageCode,
            playbookState.Patches,
            createPlaybookRequest.FceContents ?? new Dictionary<string, FceContent>(),
            playbookState.Cursor,
            cancellationToken);
    }

    [Authorize]
    [Route("create-from-dsl")]
    [Consumes("text/yaml", "application/x-yaml", "text/plain")]
    [HttpPost]
    public async Task<IActionResult> PostDsl(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var yaml = await reader.ReadToEndAsync(cancellationToken);

        DslCompileResult compiled;
        try
        {
            compiled = DslCompiler.Compile(yaml);
        }
        catch (DslCompilationException ex)
        {
            return BadRequest($"DSL compile error: {ex.Message}");
        }

        return await BootstrapPlaybookAsync(
            compiled.Party,
            compiled.ServiceResource,
            compiled.InitialTitle,
            compiled.InitialSummary,
            compiled.Language,
            compiled.Blueprint.Patches,
            compiled.Blueprint.FceContents,
            compiled.InitialCursor,
            cancellationToken);
    }

    private async Task<IActionResult> BootstrapPlaybookAsync(
        string party,
        string serviceResource,
        string? initialTitleOverride,
        string? initialSummaryOverride,
        string? initialLanguageOverride,
        JsonArray patches,
        IReadOnlyDictionary<string, FceContent> fceContents,
        int initialCursor,
        CancellationToken cancellationToken)
    {
        var initialTitle = string.IsNullOrWhiteSpace(initialTitleOverride) ? "Playbook" : initialTitleOverride;
        var initialSummary = string.IsNullOrWhiteSpace(initialSummaryOverride) ? "Playbook dialog" : initialSummaryOverride;
        var initialLanguageCode = string.IsNullOrWhiteSpace(initialLanguageOverride) ? "en" : initialLanguageOverride;

        var dto = new V1ServiceOwnerDialogsCommandsCreate_Dialog
        {
            ServiceResource = serviceResource,
            Party = party,
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

        var blueprint = new PlaybookBlueprint(dialogId, patches, fceContents);
        var stateId = await stateStore.CreateAsync(blueprint, cancellationToken);

        var compiler = new PlaybookCompiler(options.Value)
        {
            Progress = 0,
            SessionVars = blueprint.InitialVars
        };
        var compiledPatches = await compiler.CompilePatches(stateId, blueprint, initialCursor);
        if (compiledPatches.Count == 0)
        {
            return BadRequest("Cursor produced no patches.");
        }

        var patchResult = await dialogporten.V1ServiceOwnerDialogsPatchDialog(dialogId, compiledPatches, null, cancellationToken);
        if (!patchResult.IsSuccessful)
        {
            logger.LogWarning(
                "Dialogporten PATCH /dialogs/{DialogId} (bootstrap stage {Cursor}) returned {StatusCode}. Body: {Body}",
                dialogId, initialCursor, (int)patchResult.StatusCode, patchResult.Error?.Content);
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
