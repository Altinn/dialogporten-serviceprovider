using System.IO;
using System.Text.Json.Nodes;
using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Clients;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook;
using Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;
using Digdir.BDB.Dialogporten.ServiceProvider.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("playbook")]
[EnableCors("AllowedOriginsPolicy")]
public class PlaybookController(
    IDialogportenApiProvider apiProvider,
    IPlaybookStateStore stateStore,
    IDialogportenEnvironmentRegistry environments,
    IOptions<ServiceProviderSettings> options,
    ILogger<PlaybookController> logger) : ControllerBase
{
    /// <summary>Lists the environments a playbook can be created in.</summary>
    [Authorize]
    [Route("environments")]
    [HttpGet]
    public IActionResult GetEnvironments() => Ok(new
    {
        @default = environments.Default.Key,
        environments = environments.All.Select(e => new
        {
            key = e.Key,
            displayName = e.DisplayName,
            dialogportenBaseUri = e.DialogportenBaseUri,
            afUri = e.AfUri
        })
    });

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
            createPlaybookRequest.Environment,
            createPlaybookRequest.Party,
            createPlaybookRequest.ServiceResource,
            createPlaybookRequest.InitialTitle,
            createPlaybookRequest.InitialSummary,
            createPlaybookRequest.InitialLanguageCode,
            new PlaybookBlueprint(
                Guid.Empty,
                playbookState.Patches,
                createPlaybookRequest.FceContents ?? new Dictionary<string, FceContent>()),
            playbookState.Cursor,
            cancellationToken);
    }

    [Authorize]
    [Route("create-from-dsl")]
    [Consumes("text/yaml", "application/x-yaml", "text/plain")]
    [HttpPost]
    public async Task<IActionResult> PostDsl(
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
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
            environment,
            compiled.Party,
            compiled.ServiceResource,
            compiled.InitialTitle,
            compiled.InitialSummary,
            compiled.Language,
            compiled.Blueprint,
            compiled.InitialCursor,
            cancellationToken);
    }

    private async Task<IActionResult> BootstrapPlaybookAsync(
        string? environmentKey,
        string party,
        string serviceResource,
        string? initialTitleOverride,
        string? initialSummaryOverride,
        string? initialLanguageOverride,
        PlaybookBlueprint blueprintTemplate,
        int initialCursor,
        CancellationToken cancellationToken)
    {
        if (!environments.TryResolve(environmentKey, out var environment))
        {
            return BadRequest(
                $"Unknown environment '{environmentKey}'. Valid values: {string.Join(", ", environments.All.Select(e => e.Key))}.");
        }
        var dialogporten = apiProvider.GetApi(environment);

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
                "Dialogporten ({Environment}) POST /dialogs returned {StatusCode}. Body: {Body}",
                environment.Key, (int)dialogResult.StatusCode, dialogResult.Error?.Content);
            return BadRequest(dialogResult.Error?.Content);
        }

        if (!Guid.TryParse(dialogResult.Content!, out var dialogId))
        {
            return BadRequest("Parse Guid failed");
        }

        // Bind the freshly created dialog id onto the compiled blueprint. Previously this
        // rebuilt the blueprint via the Phase A constructor, silently dropping InitialVars and
        // StageBehaviors for DSL-created playbooks.
        var blueprint = blueprintTemplate with { DialogId = dialogId, EnvironmentKey = environment.Key };
        var stateId = await stateStore.CreateAsync(blueprint, cancellationToken);

        var compiler = new PlaybookCompiler(options.Value)
        {
            Progress = 0,
            SessionVars = blueprint.InitialVars,
            StageCursors = blueprint.StageCursors
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

        return Ok(new
        {
            dialogId,
            stateId,
            environment = environment.Key,
            inboxUrl = environment.InboxUrl(dialogId)
        });
    }

}

public class CreatePlaybookRequest
{
    /// <summary>Environment key (eg. tt02, at23, local). Omitted means the configured default.</summary>
    public string? Environment { get; set; }
    public string Party { get; set; } = null!;
    public string ServiceResource { get; set; } = null!;
    public PlaybookState PlaybookState { get; set; } = null!;
    public Dictionary<string, FceContent>? FceContents { get; set; }
    public string? InitialTitle { get; set; }
    public string? InitialSummary { get; set; }
    public string? InitialLanguageCode { get; set; }
}
