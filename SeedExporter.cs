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
        if (createDialogCommand.Content.Title?.Value != null)
        {
            seedParts.Add($"title:mediatype={createDialogCommand.Content.Title.MediaType}");
            seedParts
                .AddRange(createDialogCommand.Content.Title.Value
                                             .Select(localization =>
                                                 $"title:{localization.LanguageCode}={localization.Value}"));
        }

        if (createDialogCommand.Content.Summary?.Value != null)
        {
            seedParts.AddRange(createDialogCommand.Content.Summary.Value
                                                  .Select(localization =>
                                                      $"summary:{localization.LanguageCode}={localization.Value}"));
        }

        if (createDialogCommand.Content.NonSensitiveTitle?.Value != null)
        {
            seedParts
                .AddRange(createDialogCommand.Content.NonSensitiveTitle.Value
                                             .Select(localization =>
                                                 $"non-sensitive-title:{localization.LanguageCode}={localization.Value}"));
        }

        if (createDialogCommand.Content.NonSensitiveSummary?.Value != null)
        {
            seedParts.AddRange(createDialogCommand.Content.NonSensitiveSummary.Value
                                                  .Select(localization =>
                                                      $"non-sensitive-summary:{localization.LanguageCode}={localization.Value}"));
        }

        if (createDialogCommand.Content.SenderName?.Value != null)
        {
            seedParts.AddRange(createDialogCommand.Content.SenderName.Value
                                                  .Select(localization =>
                                                      $"sendername:{localization.LanguageCode}={localization.Value}"));
        }

        if (createDialogCommand.Content.AdditionalInfo?.Value != null)
        {
            seedParts.AddRange(createDialogCommand.Content.AdditionalInfo.Value
                                                  .Select(localization =>
                                                      $"additional-info:{localization.LanguageCode}={localization.Value}"));
        }

        if (createDialogCommand.Content.ExtendedStatus?.Value != null)
        {
            seedParts.AddRange(createDialogCommand.Content.ExtendedStatus.Value
                                                  .Select(localization =>
                                                      $"extended-status:{localization.LanguageCode}={localization.Value}"));
        }

        if (createDialogCommand.Content.MainContentReference?.Value != null)
        {
            seedParts.AddRange(createDialogCommand.Content.MainContentReference.Value
                                                  .Select(localization =>
                                                      $"main-content-reference:{localization.LanguageCode}={localization.Value}"));
        }

        // Collections
        // Search Tags
        seedParts.AddRange(createDialogCommand.SearchTags.Select(tag => $"searchtag={tag.Value}"));

        return string.Join(";", seedParts);
    }
}
