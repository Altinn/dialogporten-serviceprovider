using Altinn.ApiClients.Dialogporten.Features.V1;

namespace Digdir.BDB.Dialogporten.ServiceProvider;

public static class SeedExporter
{
    public static string ExportSeed(V1ServiceOwnerDialogsCommandsCreate_Dialog createDialogCommand)
    {
        var seedParts = new List<string>();

        // Basic Dialog Information
        if (createDialogCommand.Id.HasValue)
            seedParts.Add($"id={createDialogCommand.Id}");

        if (!string.IsNullOrEmpty(createDialogCommand.IdempotentKey))
            seedParts.Add($"idempotentkey={createDialogCommand.IdempotentKey}");

        if (!string.IsNullOrEmpty(createDialogCommand.ServiceResource))
            seedParts.Add($"serviceresource={createDialogCommand.ServiceResource}");

        if (!string.IsNullOrEmpty(createDialogCommand.Party))
            seedParts.Add($"party={createDialogCommand.Party}");

        if (createDialogCommand.Progress.HasValue)
            seedParts.Add($"progress={createDialogCommand.Progress}");

        if (!string.IsNullOrEmpty(createDialogCommand.ExtendedStatus))
            seedParts.Add($"extendedstatus={createDialogCommand.ExtendedStatus}");

        if (!string.IsNullOrEmpty(createDialogCommand.ExternalReference))
            seedParts.Add($"externalreference={createDialogCommand.ExternalReference}");

        if (createDialogCommand.VisibleFrom.HasValue)
            seedParts.Add($"visiblefrom={createDialogCommand.VisibleFrom.Value:o}");

        if (createDialogCommand.DueAt.HasValue)
            seedParts.Add($"dueat={createDialogCommand.DueAt.Value:o}");

        if (!string.IsNullOrEmpty(createDialogCommand.Process))
            seedParts.Add($"process={createDialogCommand.Process}");

        if (!string.IsNullOrEmpty(createDialogCommand.PrecedingProcess))
            seedParts.Add($"precedingprocess={createDialogCommand.PrecedingProcess}");

        if (createDialogCommand.ExpiresAt.HasValue)
            seedParts.Add($"expiresat={createDialogCommand.ExpiresAt.Value:o}");

        seedParts.Add($"isapionly={createDialogCommand.IsApiOnly.ToString().ToLower()}");

        if (createDialogCommand.CreatedAt.HasValue)
            seedParts.Add($"createdat={createDialogCommand.CreatedAt.Value:o}");

        if (createDialogCommand.UpdatedAt.HasValue)
            seedParts.Add($"updatedat={createDialogCommand.UpdatedAt.Value:o}");

        seedParts.Add($"status={createDialogCommand.Status}");

        if (createDialogCommand.SystemLabel.HasValue)
            seedParts.Add($"systemlabel={createDialogCommand.SystemLabel}");

        // Content fields
        ExportContent(seedParts, createDialogCommand.Content);

        // Collections

        // Search Tags
        seedParts.AddRange(createDialogCommand.SearchTags.Select(tag => $"searchtag={tag.Value}"));

        // Attachments
        ExportAttachments(seedParts, createDialogCommand.Attachments);

        // Transmissions
        ExportTransmissions(seedParts, createDialogCommand.Transmissions);

        return string.Join(";", seedParts);
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
                parts.Add($"{myField}:url:{index}:url={url.Url}");
                parts.Add($"{myField}:url:{index}:mediatype={url.MediaType}");
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
                parts.Add($"{field}:url:{index}:url={url.Url}");
                parts.Add($"{field}:url:{index}:mediatype={url.MediaType}");
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
        parts.Add($"{field}:mediatype={Uri.EscapeDataString(contentValue.MediaType)}");
        parts.AddRange(contentValue.Value.Select(localization => $"{field}:{localization.LanguageCode}={localization.Value}"));
    }
}
