using Altinn.ApiClients.Dialogporten.Features.V1;

namespace Digdir.BDB.Dialogporten.ServiceProvider;

public static class PrefillExtensions
{
    public static void Prefill(this V1ServiceOwnerDialogsCommandsCreate_GuiAction guiAction, GuiActionPrefill prefillType)
    {
        switch (prefillType)
        {
            case GuiActionPrefill.Empty:
                guiAction.Title = [];
                guiAction.Prompt = [];
                guiAction.Url = null!;
                guiAction.IsDeleteDialogAction = false;
                guiAction.Action = "";
                break;
            case GuiActionPrefill.ReadConfirmation:
                guiAction.Title =
                [
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Bekreft lest",
                        LanguageCode = "nb"
                    },
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Erkjet lest",
                        LanguageCode = "nn"
                    },
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Confirm read",
                        LanguageCode = "en"
                    }
                ];
                guiAction.Prompt = [];
                guiAction.Url = new Uri("https://dialogporten-serviceprovider-ahb4fkchhgceevej.norwayeast-01.azurewebsites.net/guiaction/write?addActivity=true");
                guiAction.Action = "write";
                guiAction.HttpMethod = Http_HttpVerb.POST;
                guiAction.IsDeleteDialogAction = false;
                break;
            case GuiActionPrefill.DeleteButton:

                guiAction.Title =
                [
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Slett dialog",
                        LanguageCode = "nb"
                    },
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Slett samtale",
                        LanguageCode = "nn"
                    },
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Delete Dialogue",
                        LanguageCode = "en"
                    }
                ];

                guiAction.Prompt =
                [
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Er du sikker?",
                        LanguageCode = "nb"
                    },
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Vil du dette?",
                        LanguageCode = "nn"
                    },
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Are you sure?",
                        LanguageCode = "en"
                    },
                ];
                guiAction.Url = new Uri("https://dialogporten-serviceprovider-ahb4fkchhgceevej.norwayeast-01.azurewebsites.net/guiaction/write/");
                guiAction.Action = "write";
                guiAction.HttpMethod = Http_HttpVerb.DELETE;
                guiAction.IsDeleteDialogAction = true;
                break;
            case GuiActionPrefill.Information:

                guiAction.Title =
                [
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Slett dialog",
                        LanguageCode = "nb"
                    },
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Slett samtale",
                        LanguageCode = "nn"
                    },
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Delete Dialogue",
                        LanguageCode = "en"
                    }
                ];
                guiAction.Prompt = [];
                guiAction.Url = new Uri("https://dialogporten-serviceprovider-ahb4fkchhgceevej.norwayeast-01.azurewebsites.net/guiaction/write?addActivity=true");
                guiAction.Action = "write";
                guiAction.HttpMethod = Http_HttpVerb.DELETE;
                guiAction.IsDeleteDialogAction = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(prefillType), prefillType, null);

        }
    }
}

public enum GuiActionPrefill
{
    Empty,
    ReadConfirmation,
    DeleteButton,
    Information
}
