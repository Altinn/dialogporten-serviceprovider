using Altinn.ApiClients.Dialogporten.Features.V1;

namespace Digdir.BDB.Dialogporten.ServiceProvider;

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
public static class SeedExporter
{
    public static string ExportSeed(V1ServiceOwnerDialogsCommandsCreate_Dialog createDialogCommand)
    {
        var seedParts = new List<string>();

        // Basic Dialog Information
        if (createDialogCommand.Id.HasValue)
        {
            seedParts.Add($"id={createDialogCommand.Id}");
        }

        if (!string.IsNullOrEmpty(createDialogCommand.IdempotentKey))
        {
            seedParts.Add($"idempotentkey={createDialogCommand.IdempotentKey}");
        }

        if (!string.IsNullOrEmpty(createDialogCommand.ServiceResource))
        {
            seedParts.Add($"serviceresource={createDialogCommand.ServiceResource}");
        }

        if (!string.IsNullOrEmpty(createDialogCommand.Party))
        {
            seedParts.Add($"party={createDialogCommand.Party}");
        }

        if (createDialogCommand.Progress.HasValue)
        {
            seedParts.Add($"progress={createDialogCommand.Progress}");
        }

        if (!string.IsNullOrEmpty(createDialogCommand.ExtendedStatus))
        {
            seedParts.Add($"extendedstatus={createDialogCommand.ExtendedStatus}");
        }

        if (!string.IsNullOrEmpty(createDialogCommand.ExternalReference))
        {
            seedParts.Add($"externalreference={createDialogCommand.ExternalReference}");
        }

        if (createDialogCommand.VisibleFrom.HasValue)
        {
            seedParts.Add($"visiblefrom={createDialogCommand.VisibleFrom.Value:o}");
        }

        if (createDialogCommand.DueAt.HasValue)
        {
            seedParts.Add($"dueat={createDialogCommand.DueAt.Value:o}");
        }

        if (!string.IsNullOrEmpty(createDialogCommand.Process))
        {
            seedParts.Add($"process={createDialogCommand.Process}");
        }

        if (!string.IsNullOrEmpty(createDialogCommand.PrecedingProcess))
        {
            seedParts.Add($"precedingprocess={createDialogCommand.PrecedingProcess}");
        }

        if (createDialogCommand.ExpiresAt.HasValue)
        {
            seedParts.Add($"expiresat={createDialogCommand.ExpiresAt.Value:o}");
        }

        seedParts.Add($"isapionly={createDialogCommand.IsApiOnly.ToString().ToLower()}");

        if (createDialogCommand.CreatedAt.HasValue)
        {
            seedParts.Add($"createdat={createDialogCommand.CreatedAt.Value:o}");
        }

        if (createDialogCommand.UpdatedAt.HasValue)
        {
            seedParts.Add($"updatedat={createDialogCommand.UpdatedAt.Value:o}");
        }

        seedParts.Add($"status={createDialogCommand.Status}");

        if (createDialogCommand.SystemLabel.HasValue)
        {
            seedParts.Add($"systemlabel={createDialogCommand.SystemLabel}");
        }

        // Content fields
        ExportContent(seedParts, createDialogCommand.Content);

        // Collections

        // Search Tags
        seedParts.AddRange(createDialogCommand.SearchTags.Select(tag => $"searchtag={tag.Value}"));
        
        // ServiceOwnerLabels
        seedParts.AddRange(createDialogCommand.ServiceOwnerContext.ServiceOwnerLabels.Select(label => $"serviceownerlabel={label.Value}"));

        // Attachments
        ExportAttachments(seedParts, createDialogCommand.Attachments);

        // Transmissions
        ExportTransmissions(seedParts, createDialogCommand.Transmissions);

        // Gui Actions
        ExportGuiActions(seedParts, createDialogCommand.GuiActions);

        // Api Actions
        ExportApiActions(seedParts, createDialogCommand.ApiActions);

        // Activities
        ExportActivities(seedParts, createDialogCommand.Activities);

        return string.Join(";", seedParts);
    }
    private static void ExportActivities(List<string> seedParts, ICollection<V1ServiceOwnerDialogsCommandsCreate_Activity> activities)
    {
        var activityIndex = 1;
        foreach (var activity in activities)
        {
            var field = $"activity:{activityIndex++}";

            if (activity.Id.HasValue)
            {
                seedParts.Add($"{field}:id={activity.Id}");
            }

            if (activity.CreatedAt.HasValue)
            {
                seedParts.Add($"{field}:createdat={activity.CreatedAt.Value:o}");
            }

            seedParts.Add($"{field}:type={activity.Type}");

            if (activity.ExtendedType is not null)
            {
                seedParts.Add($"{field}:extendedtype={activity.ExtendedType}");
            }
            if (activity.TransmissionId.HasValue)
            {
                seedParts.Add($"{field}:id={activity.TransmissionId.Value}");
            }

            if (activity.PerformedBy != null)
            {
                ExportCommonActor(seedParts, activity.PerformedBy, $"{field}:performedby");
            }

            if (activity.Description is not null)
            {
                seedParts.AddRange(activity.Description.Select(localization => $"{field}:description:{localization.LanguageCode}={localization.Value}"));
            }

        }
    }
    private static void ExportApiActions(List<string> seedParts, ICollection<V1ServiceOwnerDialogsCommandsCreate_ApiAction> apiActions)
    {
        var apiActionIndex = 1;
        foreach (var apiAction in apiActions)
        {
            var field = $"apiaction:{apiActionIndex++}";

            if (apiAction.Id.HasValue)
            {
                seedParts.Add($"{field}:id={apiAction.Id}");
            }

            if (!string.IsNullOrEmpty(apiAction.Action))
            {
                seedParts.Add($"{field}:action={apiAction.Action}");
            }

            if (!string.IsNullOrEmpty(apiAction.AuthorizationAttribute))
            {
                seedParts.Add($"{field}:authorizationattribute={apiAction.AuthorizationAttribute}");
            }

            if (!string.IsNullOrEmpty(apiAction.Name))
            {
                seedParts.Add($"{field}:name={apiAction.Name}");
            }

            // API Action Endpoints
            var endpointIndex = 1;
            foreach (var endpoint in apiAction.Endpoints)
            {
                var endpointField = $"{field}:endpoint:{endpointIndex++}";

                if (!string.IsNullOrEmpty(endpoint.Version))
                {
                    seedParts.Add($"{endpointField}:version={endpoint.Version}");
                }
                if (endpoint.Url is not null)
                {
                    seedParts.Add($"{endpointField}:url={endpoint.Url}");
                }

                seedParts.Add($"{endpointField}:httpmethod={endpoint.HttpMethod}");

                if (endpoint.DocumentationUrl != null)
                {
                    seedParts.Add($"{endpointField}:documentationurl={endpoint.DocumentationUrl}");
                }

                if (endpoint.RequestSchema != null)
                {
                    seedParts.Add($"{endpointField}:requestschema={endpoint.RequestSchema}");
                }

                if (endpoint.ResponseSchema != null)
                {
                    seedParts.Add($"{endpointField}:responseschema={endpoint.ResponseSchema}");
                }

                seedParts.Add($"{endpointField}:deprecated={endpoint.Deprecated.ToString().ToLower()}");

                if (endpoint.SunsetAt.HasValue)
                {
                    seedParts.Add($"{endpointField}:sunsetat={endpoint.SunsetAt.Value:o}");
                }
            }
        }
    }
    private static void ExportGuiActions(List<string> seedParts, ICollection<V1ServiceOwnerDialogsCommandsCreate_GuiAction> guiActions)
    {
        var guiActionIndex = 1;
        foreach (var guiAction in guiActions)
        {
            var field = $"guiaction:{guiActionIndex++}";

            if (guiAction.Id.HasValue)
            {
                seedParts.Add($"{field}:id={guiAction.Id}");
            }

            if (!string.IsNullOrEmpty(guiAction.Action))
            {
                seedParts.Add($"{field}:action={guiAction.Action}");
            }

            if (guiAction.HttpMethod.HasValue)
            {
                seedParts.Add($"{field}:httpmethod={guiAction.HttpMethod}");
            }

            if (guiAction.Url is not null)
            {
                seedParts.Add($"{field}:url={guiAction.Url}");
            }

            seedParts.Add($"{field}:priority={guiAction.Priority}");

            seedParts.Add($"{field}:isdeletedialogaction={guiAction.IsDeleteDialogAction.ToString().ToLower()}");

            // Title localizations
            if (guiAction.Title.Count != 0)
            {
                seedParts.AddRange(guiAction.Title.Select(localization => $"{field}:title:{localization.LanguageCode}={localization.Value}"));
            }

            // Prompt localizations
            if (guiAction.Prompt.Count != 0)
            {
                seedParts.AddRange(guiAction.Prompt.Select(localization => $"{field}:prompt:{localization.LanguageCode}={localization.Value}"));
            }
        }
    }
    private static void ExportTransmissions(List<string> parts, ICollection<V1ServiceOwnerDialogsCommandsCreate_Transmission> transmissions)
    {
        var transmissionIndex = 1;
        foreach (var transmission in transmissions)
        {
            var field = $"transmission:{transmissionIndex++}";
            if (transmission.Id.HasValue)
            {
                parts.Add($"{field}:id={transmission.Id.Value}");
            }

            if (transmission.CreatedAt != default)
            {
                parts.Add($"{field}:createdat={transmission.CreatedAt:o}");
            }

            if (!string.IsNullOrWhiteSpace(transmission.AuthorizationAttribute))
            {
                parts.Add($"{field}:authorizationattribute={transmission.AuthorizationAttribute}");
            }

            if (transmission.RelatedTransmissionId.HasValue)
            {
                parts.Add($"{field}:relatedtransmissionid={transmission.RelatedTransmissionId.Value}");
            }

            ExportCommonActor(parts, transmission.Sender, $"{field}:sender");

            // Content
            ExportCommentContentValue(parts, $"{field}:title", transmission.Content.Title);
            ExportCommentContentValue(parts, $"{field}:summary", transmission.Content.Summary);
            ExportCommentContentValue(parts, $"{field}:contentreference", transmission.Content.ContentReference);

            // attachment
            ExportTransmissionAttachments(parts, transmission.Attachments, field);
        }
    }

    private static void ExportTransmissionAttachments(List<string> parts, ICollection<V1ServiceOwnerDialogsCommandsCreate_TransmissionAttachment> attachments, string field)
    {
        var attachmentIndex = 1;
        foreach (var attachment in attachments)
        {
            var myField = $"{field}:attachment:{attachmentIndex++}";
            if (attachment.Id.HasValue)
            {
                parts.Add($"{myField}:id={attachment.Id.Value}");
            }
            if (attachment.DisplayName.Count != 0)
            {
                parts.AddRange(attachment.DisplayName.Select(localization => $"{myField}:displayname:{localization.LanguageCode}={localization.Value}"));
            }

            var index = 1;
            foreach (var url in attachment.Urls)
            {
                if (url.Url is not null)
                {
                    parts.Add($"{field}:url:{index}:url={url.Url}");
                }
                if (!string.IsNullOrWhiteSpace(url.MediaType))
                {
                    parts.Add($"{field}:url:{index}:mediatype={url.MediaType}");
                }
                parts.Add($"{myField}:url:{index}:consumertype={url.ConsumerType}");
                index++;
            }

            attachmentIndex++;


        }
    }

    private static void ExportAttachments(List<string> parts, ICollection<V1ServiceOwnerDialogsCommandsCreate_Attachment> attachments)
    {
        var attachmentIndex = 1;
        foreach (var attachment in attachments)
        {
            var field = $"attachment:{attachmentIndex++}";
            if (attachment.Id.HasValue)
            {
                parts.Add($"{field}:id={attachment.Id}");
            }
            if (attachment.DisplayName.Count != 0)
            {
                parts.AddRange(attachment.DisplayName.Select(localization => $"{field}:displayname:{localization.LanguageCode}={localization.Value}"));
            }

            var index = 1;
            foreach (var url in attachment.Urls)
            {
                if (url.Url is not null)
                {
                    parts.Add($"{field}:url:{index}:url={url.Url}");
                }
                if (!string.IsNullOrWhiteSpace(url.MediaType))
                {
                    parts.Add($"{field}:url:{index}:mediatype={url.MediaType}");
                }
                parts.Add($"{field}:url:{index}:consumertype={url.ConsumerType}");
                index++;
            }
        }
    }

    private static void ExportCommonActor(List<string> parts, V1ServiceOwnerCommonActors_Actor actor, string field)
    {

        parts.Add($"{field}:actortype={actor.ActorType}");
        if (!string.IsNullOrWhiteSpace(actor.ActorName))
        {
            parts.Add($"{field}:actorname={actor.ActorName}");
        }

        if (!string.IsNullOrWhiteSpace(actor.ActorId))
        {
            parts.Add($"{field}:actorid={actor.ActorId}");
        }
    }

    // Amund: Escape ; in values URL enconding?
    private static void ExportContent(List<string> parts, V1ServiceOwnerDialogsCommandsCreate_Content content)
    {
        foreach (var propertyInfo in content.GetType().GetProperties())
        {
            var value = propertyInfo.GetValue(content);

            if (value == null || value.GetType() != typeof(V1CommonContent_ContentValue))
                continue;

            var contentValue = value as V1CommonContent_ContentValue;
            var field = $"content:{propertyInfo.Name.ToLowerInvariant()}";
            ExportCommentContentValue(parts, field, contentValue);
        }
    }

    private static void ExportCommentContentValue(List<string> parts, string field, V1CommonContent_ContentValue? contentValue)
    {
        if (contentValue == null)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(contentValue.MediaType))
        {
            parts.Add($"{field}:mediatype={Uri.EscapeDataString(contentValue.MediaType)}");
        }
        parts.AddRange(contentValue.Value.Select(localization => $"{field}:{localization.LanguageCode}={localization.Value}"));
    }
}
// ReSharper restore ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
