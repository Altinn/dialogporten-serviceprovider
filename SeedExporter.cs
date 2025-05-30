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
        seedParts.AddRange(ExportContent(createDialogCommand.Content));

        // Collections
        // Search Tags
        seedParts.AddRange(createDialogCommand.SearchTags.Select(tag => $"searchtag={tag.Value}"));

        seedParts.AddRange(ExportAttachments(createDialogCommand.Attachments));

        return string.Join(";", seedParts);
    }
    // Amund: Escape ; in values URL enconding?
    private static List<string> ExportContent(V1ServiceOwnerDialogsCommandsCreate_Content content)
    {
        var parts = new List<string>();
        foreach (var propertyInfo in content.GetType().GetProperties())
        {
            var value = propertyInfo.GetValue(content);

            if (value == null || value.GetType() != typeof(V1CommonContent_ContentValue))
                continue;

            var contentValue = value as V1CommonContent_ContentValue;
            var field = $"content:{propertyInfo.Name.ToLowerInvariant()}";
            parts.Add($"{field}:mediatype={Uri.EscapeDataString(contentValue!.MediaType)}");
            parts.AddRange(contentValue.Value.Select(localization => $"{field}:{localization.LanguageCode}={localization.Value}"));
        }

        return parts;
    }

    private static List<string> ExportAttachments(ICollection<V1ServiceOwnerDialogsCommandsCreate_Attachment> attachments)
    {
        var parts = new List<string>();
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
        return parts;
    }
}
