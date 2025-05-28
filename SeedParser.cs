using Altinn.ApiClients.Dialogporten.Features.V1;
using Org.BouncyCastle.Security;

namespace Digdir.BDB.Dialogporten.ServiceProvider;

public static class SeedParser
{
    public static void ParseSeed(V1ServiceOwnerDialogsCommandsCreate_Dialog createDialogCommand, string seed)
    {
        // Amund: Cleaning av CreateDialogCommand
        var parts = seed.Split(";");

        foreach (var part in parts)
        {
            var seedData = part.Split("=", 2);
            var fieldData = seedData.First().Split(":").AsEnumerable().GetEnumerator();

            fieldData.MoveNext();
            var field = fieldData.Current;
            var value = seedData[1];
            switch (field.ToLowerInvariant())
            {
                // Dialog fields
                case "id":
                    if (Guid.TryParse(value, out var dialogId))
                    {
                        createDialogCommand.Id = dialogId;
                    }
                    break;
                case "idempotentkey":
                    createDialogCommand.IdempotentKey = value;
                    break;
                case "serviceresource":
                case "resource":
                    createDialogCommand.ServiceResource = value.StartsWith("urn:altinn:resource:")
                        ? value
                        : "urn:altinn:resource:" + value;
                    break;
                case "party":
                    createDialogCommand.Party = value;
                    break;
                case "progress":
                    if (int.TryParse(value, out var progress))
                    {
                        createDialogCommand.Progress = progress;
                    }
                    break;
                case "extendedstatus":
                    createDialogCommand.ExtendedStatus = value;
                    break;
                case "externalreference":
                    createDialogCommand.ExternalReference = value;
                    break;
                case "visiblefrom":
                    if (DateTimeOffset.TryParse(value, out var visibleFrom))
                    {
                        createDialogCommand.VisibleFrom = visibleFrom;
                    }
                    break;
                case "dueat":
                    if (DateTimeOffset.TryParse(value, out var dueAt))
                    {
                        createDialogCommand.DueAt = dueAt;
                    }
                    break;
                case "process":
                    createDialogCommand.Process = value;
                    break;
                case "precedingprocess":
                    createDialogCommand.PrecedingProcess = value;
                    break;
                case "expiresat":
                    if (DateTimeOffset.TryParse(value, out var expiresAt))
                    {
                        createDialogCommand.ExpiresAt = expiresAt;
                    }
                    break;
                case "isapionly":
                    if (bool.TryParse(value, out var isApiOnly))
                    {
                        createDialogCommand.IsApiOnly = isApiOnly;
                    }
                    break;
                case "createdat":
                    if (DateTimeOffset.TryParse(value, out var createdAt))
                    {
                        createDialogCommand.CreatedAt = createdAt;
                    }
                    break;
                case "updatedat":
                    if (DateTimeOffset.TryParse(value, out var updatedAt))
                    {
                        createDialogCommand.UpdatedAt = updatedAt;
                    }
                    break;
                case "status":
                    if (Enum.TryParse<DialogsEntities_DialogStatus>(value, out var status))
                    {
                        createDialogCommand.Status = status;
                    }
                    break;
                case "systemlabel":
                    if (Enum.TryParse<DialogEndUserContextsEntities_SystemLabel>(value, out var systemLabel))
                    {
                        createDialogCommand.SystemLabel = systemLabel;
                    }
                    break;

                // Content fields
                case "content":
                    ParseContent(createDialogCommand, fieldData, value);
                    break;
                // Collections
                case "searchtag":
                    createDialogCommand.SearchTags.Add(
                        new V1ServiceOwnerDialogsCommandsCreate_Tag
                        {
                            Value = value
                        });
                    break;
                case "attachment":
                    ParseAttachment(createDialogCommand, fieldData, value);
                    break;
                case "guiaction":
                    ParseGuiAction(createDialogCommand, fieldData, value);
                    break;
                case "transmission":
                    ParseTransmission(createDialogCommand, fieldData, value);
                    break;
                case "apiaction":
                    ParseApiAction(createDialogCommand, fieldData, value);
                    break;
                case "activity":
                    ParseActivity(createDialogCommand, fieldData, value);
                    break;
                default:
                    throw new InvalidParameterException($"Field: {seedData.First()}, is not supported");
            }
        }
    }

    private static void ParseContent(V1ServiceOwnerDialogsCommandsCreate_Dialog createDialogCommand, IEnumerator<string> data, string value)
    {
        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.Content, value:{value}");
        }
        var field = data.Current;
        switch (field)
        {
            case "title":
                createDialogCommand.Content.Title ??= new V1CommonContent_ContentValue { Value = [] };
                AddContentValue(createDialogCommand.Content.Title, data, value);
                break;
            case "summary":
                createDialogCommand.Content.Summary ??= new V1CommonContent_ContentValue { Value = [] };
                AddContentValue(createDialogCommand.Content.Summary, data, value);
                break;
            case "non-sensitive-title":
                createDialogCommand.Content.NonSensitiveTitle ??= new V1CommonContent_ContentValue { Value = [] };
                AddContentValue(createDialogCommand.Content.NonSensitiveTitle, data, value);
                break;
            case "non-sensitive-summary":
                createDialogCommand.Content.NonSensitiveSummary ??= new V1CommonContent_ContentValue { Value = [] };
                AddContentValue(createDialogCommand.Content.NonSensitiveSummary, data, value);
                break;
            case "sender":
            case "sendername":
                createDialogCommand.Content.SenderName ??= new V1CommonContent_ContentValue { Value = [] };
                AddContentValue(createDialogCommand.Content.SenderName, data, value);
                break;
            case "additional-info":
                createDialogCommand.Content.AdditionalInfo ??= new V1CommonContent_ContentValue { Value = [] };
                AddContentValue(createDialogCommand.Content.AdditionalInfo, data, value);
                break;
            case "extended-status":
                createDialogCommand.Content.ExtendedStatus ??= new V1CommonContent_ContentValue { Value = [] };
                AddContentValue(createDialogCommand.Content.ExtendedStatus, data, value);
                break;
            case "main-content-reference":
                createDialogCommand.Content.MainContentReference ??= new V1CommonContent_ContentValue { Value = [] };
                AddContentValue(createDialogCommand.Content.MainContentReference, data, value);
                break;
            default:
                throw new InvalidParameterException($"Content Field: {field}, is not supported");
        }
    }
    private static void ParseGuiAction(V1ServiceOwnerDialogsCommandsCreate_Dialog createDialogCommand, IEnumerator<string> data, string value)
    {
        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.GuiAction, value:{value}");
        }
        if (!int.TryParse(data.Current, out var index))
        {
            throw new InvalidParameterException($"Can't {data.Current} to an Int");
        }
        var guiActions = createDialogCommand.GuiActions.ToList();
        while (guiActions.Count < index)
        {
            guiActions.Add(new V1ServiceOwnerDialogsCommandsCreate_GuiAction
            {
                Title = [],
                Prompt = []
            });
        }
        var guiAction = guiActions[index - 1];

        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.GuiAction, value:{value}");
        }
        var field = data.Current;
        switch (field)
        {
            case "id":
                Guid.TryParse(value, out var guid);
                guiAction.Id = guid;
                break;
            case "action":
                guiAction.Action = value;
                break;
            case "authorizationattribute":
                guiAction.AuthorizationAttribute = value;
                break;
            case "url":
                if (Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri))
                {
                    guiAction.Url = uri;
                }
                break;
            case "isdeletedialogaction":
                if (bool.TryParse(value, out var result))
                {
                    guiAction.IsDeleteDialogAction = result;
                }
                break;

            case "httpmethod":
                if (Enum.TryParse<Http_HttpVerb>(value, out var httpMethod))
                {
                    guiAction.HttpMethod = httpMethod;
                }
                break;
            case "priority":
                if (Enum.TryParse<DialogsEntitiesActions_DialogGuiActionPriority>(value, out var priority))
                {
                    guiAction.Priority = priority;
                }
                break;
            case "title":
                var titleLang = data.MoveNext() ? data.Current : "nb";
                var title = new V1CommonLocalizations_Localization
                {
                    Value = value,
                    LanguageCode = titleLang
                };
                guiAction.Title.Add(title);
                break;
            case "prompt":
                var promptLang = data.MoveNext() ? data.Current : "nb";
                var prompt = new V1CommonLocalizations_Localization
                {
                    Value = value,
                    LanguageCode = promptLang
                };
                guiAction.Prompt.Add(prompt);
                break;

        }
        createDialogCommand.GuiActions = guiActions;
    }
    private static void ParseAttachment(V1ServiceOwnerDialogsCommandsCreate_Dialog createDialogCommand, IEnumerator<string> data, string value)
    {
        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.Attachment, value:{value}");
        }
        if (!int.TryParse(data.Current, out var attachmentIndex))
        {
            throw new InvalidParameterException($"Can't {data.Current} to an Int");
        }

        var attachments = createDialogCommand.Attachments.ToList();
        while (attachments.Count < attachmentIndex)
        {
            attachments.Add(new V1ServiceOwnerDialogsCommandsCreate_Attachment
            {
                Urls = [],
                DisplayName = []
            });
        }
        var attachment = attachments[attachmentIndex - 1];
        var attachmentUrl = attachment.Urls?.FirstOrDefault() ?? new
            V1ServiceOwnerDialogsCommandsCreate_AttachmentUrl();

        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.Attachment, value:{value}");
        }
        var field = data.Current;
        switch (field)
        {
            case "id":
                if (Guid.TryParse(value, out var guid))
                {
                    attachment.Id = guid;
                }
                break;
            case "url":
                if (Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri))
                {
                    attachmentUrl.Url = uri;
                }
                break;
            case "mediatype":
                attachmentUrl.MediaType = value;
                break;
            case "type":
                attachmentUrl.ConsumerType = Enum.Parse
                    <Attachments_AttachmentUrlConsumerType>(value);
                break;
            case "displayname":
            case "name":
                var nameLang = data.MoveNext() ? data.Current : "nb";
                var name = new V1CommonLocalizations_Localization
                {
                    Value = value,
                    LanguageCode = nameLang
                };
                attachment.DisplayName.Add(name);
                break;
            default:
                break;
        }

        if (attachment.Urls is null)
        {
            attachment.Urls = [attachmentUrl];
        }
        else
        {
            attachment.Urls.Add(attachmentUrl);
        }
        createDialogCommand.Attachments = attachments;

    }
    private static void ParseTransmission(V1ServiceOwnerDialogsCommandsCreate_Dialog createDialogCommand, IEnumerator<string> data, string value)
    {
        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.Transmission, value:{value}");
        }
        if (!int.TryParse(data.Current, out var transmissionIndex))
        {
            throw new InvalidParameterException($"Can't {data.Current} to an Int");
        }

        var transmissions = createDialogCommand.Transmissions.ToList();
        while (transmissions.Count < transmissionIndex)
        {
            transmissions.Add(new V1ServiceOwnerDialogsCommandsCreate_Transmission
            {
                Type = DialogsEntitiesTransmissions_DialogTransmissionType.Information,
                Sender = new V1ServiceOwnerCommonActors_Actor
                {
                    ActorType = Actors_ActorType.ServiceOwner
                },
                Content = new V1ServiceOwnerDialogsCommandsCreate_TransmissionContent
                {
                    Title = new V1CommonContent_ContentValue { Value = [] },
                    Summary = new V1CommonContent_ContentValue { Value = [] },
                    ContentReference = new V1CommonContent_ContentValue { Value = [] }
                },
                Attachments = []
            });
        }
        var transmission = transmissions[transmissionIndex - 1];
        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.Transmission, value:{value}");
        }
        var field = data.Current;

        switch (field)
        {
            case "id":
                if (Guid.TryParse(value, out var guid))
                {
                    transmission.Id = guid;
                }
                break;
            case "createdat":
                if (DateTime.TryParse(value, out var dateTime))
                {
                    transmission.CreatedAt = dateTime;
                }
                break;
            case "authorizationattribute":
                transmission.AuthorizationAttribute = value;
                break;
            case "extendedtype":
                if (Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri))
                {
                    transmission.ExtendedType = uri;
                }
                break;
            case "relatedtransmissionid":
                if (Guid.TryParse(value, out var relatedId))
                {
                    transmission.RelatedTransmissionId = relatedId;
                }
                break;
            case "type":
                if (Enum.TryParse<DialogsEntitiesTransmissions_DialogTransmissionType>(value, out var type))
                {
                    transmission.Type = type;
                }
                break;
            case "sendertype":
                if (Enum.TryParse<Actors_ActorType>(value, out var actorType))
                {
                    transmission.Sender.ActorType = actorType;
                }
                break;
            case "senderid":
                transmission.Sender.ActorId = value;
                break;
            case "sendername":
                transmission.Sender.ActorName = value;
                break;
            case "title":
                transmission.Content.Title ??= new V1CommonContent_ContentValue { Value = [] };
                AddContentValue(transmission.Content.Title, data, value);
                break;
            case "summary":
                transmission.Content.Summary ??= new V1CommonContent_ContentValue { Value = [] };
                AddContentValue(transmission.Content.Summary, data, value);
                break;
            case "contentreference":
                transmission.Content.ContentReference ??= new V1CommonContent_ContentValue { Value = [] };
                AddContentValue(transmission.Content.ContentReference, data, value);
                break;
            default:
                throw new InvalidParameterException($"Transmission Field: {data.Current}, is not supported");
        }
        createDialogCommand.Transmissions = transmissions;
    }
    private static void ParseApiAction(V1ServiceOwnerDialogsCommandsCreate_Dialog createDialogCommand, IEnumerator<string> data, string value)
    {
        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.ApiAction, value:{value}");
        }
        if (!int.TryParse(data.Current, out var apiActionIndex))
        {
            throw new InvalidParameterException($"Can't {data.Current} to an Int");
        }

        var apiActions = createDialogCommand.ApiActions.ToList();
        while (apiActions.Count < apiActionIndex)
        {
            apiActions.Add(new V1ServiceOwnerDialogsCommandsCreate_ApiAction
            {
                Action = string.Empty,
                AuthorizationAttribute = string.Empty,
                Name = string.Empty,
                Endpoints = []
            });
        }
        var apiAction = apiActions[apiActionIndex - 1];

        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.ApiAction, value:{value}");
        }
        var field = data.Current;

        switch (field)
        {
            case "id":
                if (Guid.TryParse(value, out var guid))
                {
                    apiAction.Id = guid;
                }
                break;
            case "action":
                apiAction.Action = value;
                break;
            case "authorizationattribute":
                apiAction.AuthorizationAttribute = value;
                break;
            case "name":
                apiAction.Name = value;
                break;
            case "endpoint":
                ParseApiActionEndpoint(data, value, apiAction);
                break;
            default:
                throw new InvalidParameterException($"ApiAction Field: {field}, is not supported");
        }

        createDialogCommand.ApiActions = apiActions;
    }
    private static void ParseApiActionEndpoint(IEnumerator<string> data,
        string value,
        V1ServiceOwnerDialogsCommandsCreate_ApiAction apiAction)
    {
        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.ApiAction.Endpoint, value:{value}");
        }

        if (!int.TryParse(data.Current, out var endpointIndex))
        {
            throw new InvalidParameterException($"Can't {data.Current} to an Int");
        }

        var endpoints = apiAction.Endpoints.ToList();
        while (endpoints.Count < endpointIndex)
        {
            endpoints.Add(new V1ServiceOwnerDialogsCommandsCreate_ApiActionEndpoint());
        }
        var endpoint = endpoints[endpointIndex - 1];

        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.ApiAction.Endpoint, value:{value}");
        }

        var field = data.Current;

        switch (field)
        {
            case "url":
                if (Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri))
                {
                    endpoint.Url = uri;
                }
                break;
            case "httpmethod":
                if (Enum.TryParse<Http_HttpVerb>(value, out var httpMethod))
                {
                    endpoint.HttpMethod = httpMethod;
                }
                break;
            case "version":
                endpoint.Version = value;
                break;
            case "documentationurl":
                if (Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var docUri))
                {
                    endpoint.DocumentationUrl = docUri;
                }
                break;
            case "requestschema":
                if (Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var reqUri))
                {
                    endpoint.RequestSchema = reqUri;
                }
                break;
            case "responseschema":
                if (Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var respUri))
                {
                    endpoint.ResponseSchema = respUri;
                }
                break;
            case "deprecated":
                if (bool.TryParse(value, out var deprecated))
                {
                    endpoint.Deprecated = deprecated;
                }
                break;
            case "sunsetat":
                if (DateTimeOffset.TryParse(value, out var sunsetAt))
                {
                    endpoint.SunsetAt = sunsetAt;
                }
                break;
            default:
                throw new InvalidParameterException($"ApiActinEndpoint Field: {field}, is not supported");
        }

        apiAction.Endpoints = endpoints;
    }
    private static void ParseActivity(V1ServiceOwnerDialogsCommandsCreate_Dialog createDialogCommand, IEnumerator<string> data, string value)
    {
        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.ApiAction.Activity, value:{value}");
        }
        if (!int.TryParse(data.Current, out var activityIndex))
        {
            throw new InvalidParameterException($"Can't {data.Current} to an Int");
        }

        var activities = createDialogCommand.Activities.ToList();
        while (activities.Count < activityIndex)
        {
            activities.Add(new V1ServiceOwnerDialogsCommandsCreate_Activity
            {
                PerformedBy = new V1ServiceOwnerCommonActors_Actor(),
                Description = []
            });
        }
        var activity = activities[activityIndex - 1];
        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.ApiAction.Activity, value:{value}");
        }
        var field = data.Current;

        switch (field)
        {
            case "id":
                if (Guid.TryParse(value, out var guid))
                {
                    activity.Id = guid;
                }
                break;
            case "createdat":
                if (DateTimeOffset.TryParse(value, out var createdAt))
                {
                    activity.CreatedAt = createdAt;
                }
                break;
            case "extendedtype":
                if (Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var extendedType))
                {
                    activity.ExtendedType = extendedType;
                }
                break;
            case "type":
                if (Enum.TryParse<DialogsEntitiesActivities_DialogActivityType>(value, out var type))
                {
                    activity.Type = type;
                }
                break;
            case "transmissionid":
                if (Guid.TryParse(value, out var transmissionId))
                {
                    activity.TransmissionId = transmissionId;
                }
                break;
            case "performedby":
                ParseActivityPerformedBy(data, value, activity);
                break;
            case "description":
                var descriptionLang = data.MoveNext() ? data.Current : "nb";
                var description = new V1CommonLocalizations_Localization
                {
                    Value = value,
                    LanguageCode = descriptionLang
                };
                activity.Description = activity.Description.Append(description).ToList();
                break;
            default:
                throw new InvalidParameterException($"Activity Field: {field}, is not supported");
        }

        createDialogCommand.Activities = activities;
    }
    private static void ParseActivityPerformedBy(IEnumerator<string> data,
        string value,
        V1ServiceOwnerDialogsCommandsCreate_Activity activity)
    {
        if (!data.MoveNext())
        {
            throw new InvalidParameterException($"Not enough parameters for Dialog.ApiAction.Activity.PerformedBy, value:{value}");
        }

        var field = data.Current;

        switch (field)
        {
            case "actortype":
                if (Enum.TryParse<Actors_ActorType>(value, out var actorType))
                {
                    activity.PerformedBy.ActorType = actorType;
                }
                break;
            case "actorid":
                activity.PerformedBy.ActorId = value;
                break;
            case "actorname":
                activity.PerformedBy.ActorName = value;
                break;
            default:
                throw new InvalidParameterException($"Performedby Field: {field}, is not supported");
        }
    }
    private static void AddContentValue(V1CommonContent_ContentValue contentValue, IEnumerator<string> fieldData, string value)
    {
        var lang = "nb";
        if (fieldData.MoveNext())
        {
            lang = fieldData.Current;
        }
        if (lang == "mediatype")
        {
            contentValue.MediaType = lang;
            return;
        }
        contentValue.Value.Add(new V1CommonLocalizations_Localization
        {
            Value = value,
            LanguageCode = lang
        });
    }
}
