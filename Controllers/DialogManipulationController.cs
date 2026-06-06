using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Altinn.ApiClients.Dialogporten;
using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("dialog/manipulate")]
[Authorize(AuthenticationSchemes = "DialogToken")]
[EnableCors("AllowedOriginsPolicy")]
public sealed class DialogManipulationController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceownerApi _dialogporten;
    private readonly IBackgroundTaskQueue _taskQueue;

    public DialogManipulationController(IServiceownerApi dialogporten, IBackgroundTaskQueue taskQueue)
    {
        _dialogporten = dialogporten;
        _taskQueue = taskQueue;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromQuery] ManipulationQuery query)
    {
        var xacmlAction = string.IsNullOrWhiteSpace(query.XacmlAction) ? "write" : query.XacmlAction!;
        if (!IsAuthorized(xacmlAction))
        {
            return Forbid();
        }

        var manipulation = new DialogManipulation();
        ApplyQuery(query, manipulation);

        if (!string.IsNullOrWhiteSpace(query.EncodedDto))
        {
            if (!TryApplyDto(query.EncodedDto!, manipulation, out var error))
            {
                return BadRequest(error);
            }
        }

        var operations = BuildOperations(manipulation);
        if (operations.Count == 0)
        {
            return BadRequest("No manipulations requested.");
        }

        return await PerformMaybeBackgroundOperation(
            query.QueueInBackground,
            () => _dialogporten.V1ServiceOwnerDialogsPatchDialog(GetDialogId(), operations, null, CancellationToken.None));
    }

    private void ApplyQuery(ManipulationQuery query, DialogManipulation manipulation)
    {
        manipulation.Title = MergeContent(manipulation.Title, BuildContentFromQuery(query.Title, query.TitleLang, null, "text/plain"));
        manipulation.Summary = MergeContent(manipulation.Summary, BuildContentFromQuery(query.Summary, query.SummaryLang, null, "text/plain"));
        manipulation.AdditionalInfo = MergeContent(manipulation.AdditionalInfo, BuildContentFromQuery(query.AdditionalInfo, query.AdditionalInfoLang, query.AdditionalInfoMediaType, "text/markdown"));
        manipulation.MainContentReference = MergeContent(manipulation.MainContentReference, BuildContentFromQuery(query.Fce, query.FceLang, query.FceMediaType, "text/uri-list"));

        foreach (var rawTransmission in query.Transmission)
        {
            if (TryParseTransmission(rawTransmission, out var transmission))
            {
                manipulation.Transmissions.Add(transmission);
            }
        }

        foreach (var rawAttachment in query.Attachment)
        {
            if (TryParseAttachment(rawAttachment, out var attachment))
            {
                manipulation.Attachments.Add(attachment);
            }
        }

        foreach (var rawActivity in query.Activity)
        {
            if (TryParseActivity(rawActivity, out var activity))
            {
                manipulation.Activities.Add(activity);
            }
        }

        foreach (var rawAction in query.Action)
        {
            if (TryParseAction(rawAction, out var action))
            {
                manipulation.Actions.Add(action);
            }
        }
    }

    private bool TryApplyDto(string encodedDto, DialogManipulation manipulation, out string? error)
    {
        error = null;

        if (!TryDecodeBase64(encodedDto, out var json))
        {
            error = "Failed to decode DTO base64 payload.";
            return false;
        }

        DialogManipulationDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DialogManipulationDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            error = "Failed to parse DTO payload.";
            return false;
        }

        if (dto is null)
        {
            return true;
        }

        ApplyDto(dto, manipulation);
        return true;
    }

    private static bool TryDecodeBase64(string input, out string json)
    {
        json = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = input.Trim().Replace('-', '+').Replace('_', '/');
        var padding = normalized.Length % 4;
        if (padding == 2)
        {
            normalized += "==";
        }
        else if (padding == 3)
        {
            normalized += "=";
        }
        else if (padding == 1)
        {
            normalized += "===";
        }

        try
        {
            var bytes = Convert.FromBase64String(normalized);
            json = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void ApplyDto(DialogManipulationDto dto, DialogManipulation manipulation)
    {
        manipulation.Title = MergeContent(manipulation.Title, ToContentInput(dto.Title, "text/plain"));
        manipulation.Summary = MergeContent(manipulation.Summary, ToContentInput(dto.Summary, "text/plain"));
        manipulation.AdditionalInfo = MergeContent(manipulation.AdditionalInfo, ToContentInput(dto.AdditionalInfo, "text/markdown"));
        manipulation.MainContentReference = MergeContent(manipulation.MainContentReference, ToContentInput(dto.MainContentReference, "text/uri-list"));

        if (dto.Transmissions is not null)
        {
            foreach (var transmissionDto in dto.Transmissions)
            {
                var titleContent = ToContentInput(transmissionDto.Title, "text/plain");
                var summaryContent = ToContentInput(transmissionDto.Summary, "text/plain");

                if (titleContent is null || summaryContent is null)
                {
                    continue;
                }

                var type = DialogsEntitiesTransmissions_DialogTransmissionType.Information;
                if (!string.IsNullOrWhiteSpace(transmissionDto.Type) &&
                    Enum.TryParse(transmissionDto.Type, true, out DialogsEntitiesTransmissions_DialogTransmissionType parsedType))
                {
                    type = parsedType;
                }

                manipulation.Transmissions.Add(new TransmissionInput(titleContent, summaryContent, type));
            }
        }

        if (dto.Attachments is not null)
        {
            foreach (var attachmentDto in dto.Attachments)
            {
                var displayName = ToLocalizationInput(attachmentDto.Name);
                if (displayName is null)
                {
                    continue;
                }

                var fileName = attachmentDto.FileName?.Trim();
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                var mediaType = string.IsNullOrWhiteSpace(attachmentDto.MediaType)
                    ? null
                    : attachmentDto.MediaType;

                var consumerType = Attachments_AttachmentUrlConsumerType.Gui;
                if (!string.IsNullOrWhiteSpace(attachmentDto.ConsumerType) &&
                    Enum.TryParse(attachmentDto.ConsumerType, true, out Attachments_AttachmentUrlConsumerType parsedConsumer))
                {
                    consumerType = parsedConsumer;
                }

                manipulation.Attachments.Add(new AttachmentInput(
                    displayName,
                    fileName,
                    mediaType,
                    consumerType,
                    attachmentDto.Inline ?? false,
                    attachmentDto.Url));
            }
        }

        if (dto.Activities is not null)
        {
            foreach (var activityDto in dto.Activities)
            {
                var type = DialogsEntitiesActivities_DialogActivityType.Information;
                if (!string.IsNullOrWhiteSpace(activityDto.Type) &&
                    Enum.TryParse(activityDto.Type, true, out DialogsEntitiesActivities_DialogActivityType parsedType))
                {
                    type = parsedType;
                }

                var description = ToLocalizationInput(activityDto.Description);

                manipulation.Activities.Add(new ActivityInput(type, description));
            }
        }

        if (dto.Actions is not null)
        {
            foreach (var actionDto in dto.Actions)
            {
                var title = ToLocalizationInput(actionDto.Title);
                if (title is null)
                {
                    continue;
                }

                var actionName = string.IsNullOrWhiteSpace(actionDto.Action) ? "write" : actionDto.Action!;
                var url = string.IsNullOrWhiteSpace(actionDto.Url) ? "/" : actionDto.Url!;

                var method = Http_HttpVerb.GET;
                if (!string.IsNullOrWhiteSpace(actionDto.Method) &&
                    Enum.TryParse(actionDto.Method, true, out Http_HttpVerb parsedMethod))
                {
                    method = parsedMethod;
                }

                var prompt = ToLocalizationInput(actionDto.Prompt);
                var isDelete = actionDto.IsDelete ?? false;

                manipulation.Actions.Add(new ActionInput(actionName, title, method, url, isDelete, prompt));
            }
        }
    }

    private static ContentInput? ToContentInput(ContentDto? dto, string defaultMediaType)
    {
        if (dto is null)
        {
            return null;
        }

        var localizations = GetLocalizations(dto);
        if (localizations.Count == 0)
        {
            return null;
        }

        var content = new ContentInput
        {
            MediaType = string.IsNullOrWhiteSpace(dto.MediaType) ? defaultMediaType : dto.MediaType
        };

        foreach (var localization in localizations)
        {
            content.Localizations.Add(localization);
        }

        return content;
    }

    private static LocalizationInput? ToLocalizationInput(ContentDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var localizations = GetLocalizations(dto);
        if (localizations.Count == 0)
        {
            return null;
        }

        var input = new LocalizationInput();
        foreach (var localization in localizations)
        {
            input.Localizations.Add(localization);
        }

        return input;
    }

    private static List<LocalizedText> GetLocalizations(ContentDto dto)
    {
        var result = new List<LocalizedText>();

        if (dto.Values is { Count: > 0 })
        {
            foreach (var localized in dto.Values)
            {
                if (localized is null)
                {
                    continue;
                }

                if (MakeLocalized(localized.Value, localized.Language) is { } text)
                {
                    result.Add(text);
                }
            }
        }
        else if (MakeLocalized(dto.Value, dto.Language) is { } single)
        {
            result.Add(single);
        }

        return result;
    }

    private static ContentInput? BuildContentFromQuery(
        string? raw,
        string? explicitLang,
        string? explicitMediaType,
        string defaultMediaType)
    {
        var localized = ParseLocalized(raw, explicitLang);
        if (localized is null)
        {
            return null;
        }

        var content = new ContentInput
        {
            MediaType = string.IsNullOrWhiteSpace(explicitMediaType) ? defaultMediaType : explicitMediaType
        };
        content.Localizations.Add(localized);
        return content;
    }

    private static ContentInput? MergeContent(ContentInput? target, ContentInput? source)
    {
        if (source is null)
        {
            return target;
        }

        target ??= new ContentInput();

        if (!string.IsNullOrWhiteSpace(source.MediaType))
        {
            target.MediaType = source.MediaType;
        }

        foreach (var localization in source.Localizations)
        {
            target.Localizations.Add(localization);
        }

        return target;
    }

    private static bool TryParseTransmission(string raw, out TransmissionInput result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var segments = raw.Split('|');
        if (segments.Length < 2)
        {
            return false;
        }

        var offset = 0;
        var type = DialogsEntitiesTransmissions_DialogTransmissionType.Information;

        if (Enum.TryParse(segments[0], true, out DialogsEntitiesTransmissions_DialogTransmissionType parsedType))
        {
            type = parsedType;
            offset = 1;
        }

        if (segments.Length <= offset)
        {
            return false;
        }

        var titleLocalized = ParseLocalized(segments[offset], null);
        if (titleLocalized is null)
        {
            return false;
        }

        var summarySegmentIndex = offset + 1;
        if (segments.Length <= summarySegmentIndex)
        {
            return false;
        }

        var summaryLocalized = ParseLocalized(segments[summarySegmentIndex], null);
        if (summaryLocalized is null)
        {
            return false;
        }

        var titleContent = new ContentInput();
        titleContent.Localizations.Add(titleLocalized);

        var summaryContent = new ContentInput();
        summaryContent.Localizations.Add(summaryLocalized);

        result = new TransmissionInput(titleContent, summaryContent, type);
        return true;
    }

    private bool TryParseAttachment(string raw, out AttachmentInput result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var segments = raw.Split('|');
        if (segments.Length < 2)
        {
            return false;
        }

        var displayNameText = ParseLocalized(segments[0], null);
        if (displayNameText is null)
        {
            return false;
        }

        var displayName = new LocalizationInput();
        displayName.Localizations.Add(displayNameText);

        var fileName = segments[1].Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var mediaType = segments.Length > 2 && !string.IsNullOrWhiteSpace(segments[2])
            ? segments[2]
            : null;

        var consumerType = Attachments_AttachmentUrlConsumerType.Gui;
        if (segments.Length > 3 && Enum.TryParse(segments[3], true, out Attachments_AttachmentUrlConsumerType parsedConsumer))
        {
            consumerType = parsedConsumer;
        }

        var inline = false;
        if (segments.Length > 4 && bool.TryParse(segments[4], out var inlineParsed))
        {
            inline = inlineParsed;
        }

        string? url = null;
        if (segments.Length > 5 && !string.IsNullOrWhiteSpace(segments[5]))
        {
            url = segments[5];
        }

        result = new AttachmentInput(displayName, fileName, mediaType, consumerType, inline, url);
        return true;
    }

    private static bool TryParseActivity(string raw, out ActivityInput result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var segments = raw.Split('|');
        if (segments.Length == 0)
        {
            return false;
        }

        var type = DialogsEntitiesActivities_DialogActivityType.Information;
        if (!Enum.TryParse(segments[0], true, out type))
        {
            type = DialogsEntitiesActivities_DialogActivityType.Information;
        }

        LocalizationInput? description = null;
        if (segments.Length > 1)
        {
            var localized = ParseLocalized(segments[1], null);
            if (localized is not null)
            {
                description = new LocalizationInput();
                description.Localizations.Add(localized);
            }
        }

        result = new ActivityInput(type, description);
        return true;
    }

    private static bool TryParseAction(string raw, out ActionInput result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var segments = raw.Split('|');
        if (segments.Length < 4)
        {
            return false;
        }

        var actionName = string.IsNullOrWhiteSpace(segments[0]) ? "write" : segments[0].Trim();

        var titleLocalized = ParseLocalized(segments[1], null);
        if (titleLocalized is null)
        {
            return false;
        }

        var title = new LocalizationInput();
        title.Localizations.Add(titleLocalized);

        var method = Http_HttpVerb.GET;
        if (!Enum.TryParse(segments[2], true, out method))
        {
            method = Http_HttpVerb.GET;
        }

        var url = segments[3].Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        LocalizationInput? prompt = null;
        if (segments.Length > 4)
        {
            var promptLocalized = ParseLocalized(segments[4], null);
            if (promptLocalized is not null)
            {
                prompt = new LocalizationInput();
                prompt.Localizations.Add(promptLocalized);
            }
        }

        var isDelete = segments.Length > 5 && bool.TryParse(segments[5], out var deleteFlag) && deleteFlag;

        result = new ActionInput(actionName, title, method, url, isDelete, prompt);
        return true;
    }

    private List<JsonPatchOperations_Operation> BuildOperations(DialogManipulation manipulation)
    {
        var operations = new List<JsonPatchOperations_Operation>();

        if (manipulation.Title is { Localizations.Count: > 0 })
        {
            operations.Add(new JsonPatchOperations_Operation
            {
                Op = "replace",
                Path = "/content/title",
                Value = CreateContentValue(manipulation.Title, "text/plain")
            });
        }

        if (manipulation.Summary is { Localizations.Count: > 0 })
        {
            operations.Add(new JsonPatchOperations_Operation
            {
                Op = "replace",
                Path = "/content/summary",
                Value = CreateContentValue(manipulation.Summary, "text/plain")
            });
        }

        if (manipulation.AdditionalInfo is { Localizations.Count: > 0 })
        {
            operations.Add(new JsonPatchOperations_Operation
            {
                Op = "replace",
                Path = "/content/additionalInfo",
                Value = CreateContentValue(manipulation.AdditionalInfo, "text/markdown")
            });
        }

        if (manipulation.MainContentReference is { Localizations.Count: > 0 })
        {
            operations.Add(new JsonPatchOperations_Operation
            {
                Op = "replace",
                Path = "/content/mainContentReference",
                Value = CreateContentValue(manipulation.MainContentReference, "text/uri-list")
            });
        }

        string? cachedActorId = null;
        string ResolveActorId() => cachedActorId ??= GetActorId();

        foreach (var transmission in manipulation.Transmissions)
        {
            if (transmission.Title.Localizations.Count == 0 || transmission.Summary.Localizations.Count == 0)
            {
                continue;
            }

            operations.Add(new JsonPatchOperations_Operation
            {
                Op = "add",
                Path = "/transmissions/-",
                Value = new V1ServiceOwnerDialogsCommandsUpdate_Transmission
                {
                    Type = transmission.Type,
                    Sender = new V1ServiceOwnerCommonActors_Actor
                    {
                        ActorType = Actors_ActorType.PartyRepresentative,
                        ActorId = ResolveActorId()
                    },
                    Content = new V1ServiceOwnerDialogsCommandsUpdate_TransmissionContent
                    {
                        Title = CreateContentValue(transmission.Title, "text/plain"),
                        Summary = CreateContentValue(transmission.Summary, "text/plain")
                    }
                }
            });
        }

        foreach (var attachment in manipulation.Attachments)
        {
            if (attachment.DisplayName.Localizations.Count == 0)
            {
                continue;
            }

            var mediaType = attachment.MediaType ?? InferMediaType(attachment.FileName);
            var url = attachment.Url is not null
                ? BuildAbsoluteUri(attachment.Url)
                : GetAttachmentUrl(attachment.FileName, attachment.Inline);

            operations.Add(new JsonPatchOperations_Operation
            {
                Op = "add",
                Path = "/attachments/-",
                Value = new V1ServiceOwnerDialogsCommandsUpdate_Attachment
                {
                    DisplayName = CreateLocalizations(attachment.DisplayName),
                    Urls =
                    [
                        new V1ServiceOwnerDialogsCommandsUpdate_AttachmentUrl
                        {
                            MediaType = mediaType,
                            Url = url,
                            ConsumerType = attachment.ConsumerType
                        }
                    ]
                }
            });
        }

        foreach (var activity in manipulation.Activities)
        {
            var description = activity.Description is { Localizations.Count: > 0 }
                ? CreateLocalizations(activity.Description)
                : null;

            var updateActivity = new V1ServiceOwnerDialogsCommandsUpdate_Activity
            {
                Type = activity.Type,
                PerformedBy = new V1ServiceOwnerCommonActors_Actor
                {
                    ActorType = Actors_ActorType.PartyRepresentative,
                    ActorId = ResolveActorId()
                }
            };

            if (description is not null)
            {
                updateActivity.Description = description;
            }

            operations.Add(new JsonPatchOperations_Operation
            {
                Op = "add",
                Path = "/activities/-",
                Value = updateActivity
            });
        }

        if (manipulation.Actions.Count > 0)
        {
            var actions = new List<V1ServiceOwnerDialogsCommandsUpdate_GuiAction>();
            foreach (var action in manipulation.Actions)
            {
                if (action.Title.Localizations.Count == 0)
                {
                    continue;
                }

                var actionModel = new V1ServiceOwnerDialogsCommandsUpdate_GuiAction
                {
                    Action = action.Action,
                    HttpMethod = action.Method,
                    Url = BuildAbsoluteUri(action.Url),
                    IsDeleteDialogAction = action.IsDelete,
                    Title = CreateLocalizations(action.Title)
                };

                var promptLocalizations = action.Prompt is { Localizations.Count: > 0 }
                    ? CreateLocalizations(action.Prompt)
                    : null;

                if (promptLocalizations is not null)
                {
                    actionModel.Prompt = promptLocalizations;
                }

                actions.Add(actionModel);
            }

            if (actions.Count > 0)
            {
                operations.Add(new JsonPatchOperations_Operation
                {
                    Op = "replace",
                    Path = "/guiActions",
                    Value = actions
                });
            }
        }

        return operations;
    }

    private static V1CommonContent_ContentValue CreateContentValue(ContentInput input, string defaultMediaType)
    {
        return new V1CommonContent_ContentValue
        {
            MediaType = string.IsNullOrWhiteSpace(input.MediaType) ? defaultMediaType : input.MediaType,
            Value = input.Localizations
                .Select(l => new V1CommonLocalizations_Localization
                {
                    LanguageCode = l.Language,
                    Value = l.Value
                })
                .ToList()
        };
    }

    private static List<V1CommonLocalizations_Localization> CreateLocalizations(LocalizationInput input)
    {
        return input.Localizations
            .Select(l => new V1CommonLocalizations_Localization
            {
                LanguageCode = l.Language,
                Value = l.Value
            })
            .ToList();
    }

    private Uri GetAttachmentUrl(string fileName, bool inline)
    {
        object routeValues = inline
            ? new { filename = fileName, inline = true }
            : new { filename = fileName };

        var actionUrl = Url.Action(
            action: nameof(AttachmentController.Get),
            controller: nameof(AttachmentController).Replace("Controller", string.Empty),
            values: routeValues,
            protocol: Request.Scheme,
            host: Request.Host.ToString());

        return new Uri(actionUrl!);
    }

    private Uri BuildAbsoluteUri(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        var baseUri = new Uri($"{Request.Scheme}://{Request.Host}/");
        return new Uri(baseUri, url);
    }

    private static string InferMediaType(string fileName)
    {
        var extension = Path.GetExtension(fileName).Trim('.').ToLowerInvariant();
        return extension switch
        {
            "pdf" => "application/pdf",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "zip" => "application/zip",
            "json" => "application/json",
            "html" => "text/html",
            "txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    private bool IsAuthorized(string xacmlAction)
    {
        return User.Claims.Any(c => c.Type == "a" && c.Value.Split(';').Any(x => x == xacmlAction));
    }

    private Guid GetDialogId()
    {
        var dialogId = User.Claims.FirstOrDefault(c => c.Type == "i")?.Value;
        if (dialogId is null)
        {
            throw new InvalidOperationException("Dialog id not found in token");
        }

        return Guid.Parse(dialogId);
    }

    private string GetActorId()
    {
        var actorId = User.Claims.FirstOrDefault(c => c.Type == "c")?.Value;
        if (actorId is null)
        {
            throw new InvalidOperationException("Actor id not found in token");
        }

        return actorId;
    }

    private async Task<IActionResult> PerformMaybeBackgroundOperation(bool queueInBackground, Func<Task> operation)
    {
        if (queueInBackground)
        {
            _taskQueue.QueueBackgroundWorkItem(async token =>
            {
                await Task.Delay(1000, token);
                await operation();
            });

            return StatusCode(202);
        }

        await operation();
        return NoContent();
    }

    private static LocalizedText? ParseLocalized(string? raw, string? explicitLang)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        var language = explicitLang;

        if (language is null)
        {
            var colonIndex = value.IndexOf(':');
            if (colonIndex > 0 && colonIndex <= 5)
            {
                language = value[..colonIndex];
                value = value[(colonIndex + 1)..];
            }
        }

        language = string.IsNullOrWhiteSpace(language) ? "nb" : language.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new LocalizedText(language, value);
    }

    private static LocalizedText? MakeLocalized(string? value, string? language)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new LocalizedText(
            string.IsNullOrWhiteSpace(language) ? "nb" : language.Trim(),
            value.Trim());
    }

    public sealed class ManipulationQuery
    {
        public string? XacmlAction { get; set; }
        public bool QueueInBackground { get; set; }

        public string? Title { get; set; }
        public string? TitleLang { get; set; }

        public string? Summary { get; set; }
        public string? SummaryLang { get; set; }

        public string? AdditionalInfo { get; set; }
        public string? AdditionalInfoLang { get; set; }
        public string? AdditionalInfoMediaType { get; set; }

        public string? Fce { get; set; }
        public string? FceLang { get; set; }
        public string? FceMediaType { get; set; }

        [FromQuery(Name = "transmission")]
        public List<string> Transmission { get; set; } = [];

        [FromQuery(Name = "attachment")]
        public List<string> Attachment { get; set; } = [];

        [FromQuery(Name = "activity")]
        public List<string> Activity { get; set; } = [];

        [FromQuery(Name = "action")]
        public List<string> Action { get; set; } = [];

        [FromQuery(Name = "dto")]
        public string? EncodedDto { get; set; }
    }

    private sealed class DialogManipulation
    {
        public ContentInput? Title { get; set; }
        public ContentInput? Summary { get; set; }
        public ContentInput? AdditionalInfo { get; set; }
        public ContentInput? MainContentReference { get; set; }
        public List<TransmissionInput> Transmissions { get; } = [];
        public List<AttachmentInput> Attachments { get; } = [];
        public List<ActivityInput> Activities { get; } = [];
        public List<ActionInput> Actions { get; } = [];
    }

    private sealed class ContentInput
    {
        public string? MediaType { get; set; }
        public List<LocalizedText> Localizations { get; } = [];
    }

    private sealed class LocalizationInput
    {
        public List<LocalizedText> Localizations { get; } = [];
    }

    private sealed record LocalizedText(string Language, string Value);

    private sealed record TransmissionInput(
        ContentInput Title,
        ContentInput Summary,
        DialogsEntitiesTransmissions_DialogTransmissionType Type);

    private sealed record AttachmentInput(
        LocalizationInput DisplayName,
        string FileName,
        string? MediaType,
        Attachments_AttachmentUrlConsumerType ConsumerType,
        bool Inline,
        string? Url);

    private sealed record ActivityInput(
        DialogsEntitiesActivities_DialogActivityType Type,
        LocalizationInput? Description);

    private sealed record ActionInput(
        string Action,
        LocalizationInput Title,
        Http_HttpVerb Method,
        string Url,
        bool IsDelete,
        LocalizationInput? Prompt);

    private sealed class DialogManipulationDto
    {
        public ContentDto? Title { get; set; }
        public ContentDto? Summary { get; set; }
        public ContentDto? AdditionalInfo { get; set; }
        public ContentDto? MainContentReference { get; set; }
        public List<TransmissionDto>? Transmissions { get; set; }
        public List<AttachmentDto>? Attachments { get; set; }
        public List<ActivityDto>? Activities { get; set; }
        public List<ActionDto>? Actions { get; set; }
    }

    private sealed class TransmissionDto
    {
        public ContentDto? Title { get; set; }
        public ContentDto? Summary { get; set; }
        public string? Type { get; set; }
    }

    private sealed class AttachmentDto
    {
        public ContentDto? Name { get; set; }
        public string? FileName { get; set; }
        public string? MediaType { get; set; }
        public string? ConsumerType { get; set; }
        public bool? Inline { get; set; }
        public string? Url { get; set; }
    }

    private sealed class ActivityDto
    {
        public string? Type { get; set; }
        public ContentDto? Description { get; set; }
    }

    private sealed class ActionDto
    {
        public string? Action { get; set; }
        public ContentDto? Title { get; set; }
        public string? Method { get; set; }
        public string? Url { get; set; }
        public bool? IsDelete { get; set; }
        public ContentDto? Prompt { get; set; }
    }

    private sealed class ContentDto
    {
        public string? Value { get; set; }
        public string? Language { get; set; }
        public string? MediaType { get; set; }
        public List<LocalizedValueDto>? Values { get; set; }
    }

    private sealed class LocalizedValueDto
    {
        public string? Language { get; set; }
        public string? Value { get; set; }
    }
}
