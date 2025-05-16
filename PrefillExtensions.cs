using Altinn.ApiClients.Dialogporten.Features.V1;

namespace Digdir.BDB.Dialogporten.ServiceProvider;

public static class PrefillExtensions
{
    public static void Prefill(this V1ServiceOwnerDialogsCommandsCreate_Dialog dialog, DialogPrefill prefillType)
    {
        switch (prefillType)
        {
            case DialogPrefill.Empty:
                break;
            case DialogPrefill.Skattemeldig:
                dialog.ServiceResource = "urn:altinn:resource:ske-innrapportering-boligselskap";
                dialog.Party = "urn:altinn:person:identifier-no:20815497741";
                dialog.Content.Title.MediaType = "text/plain";
                dialog.Content.Title.Value =
                [
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Skattemeldingen for 2024",
                        LanguageCode = "nb"
                    }
                ];
                dialog.Content.Summary.MediaType = "text/plain";
                dialog.Content.Summary.Value =
                [
                    new V1CommonLocalizations_Localization
                    {
                        Value = "Dette er skattemeldingen din for 2024. Informasjonen er delt med deg 2025-5-16 10:22",
                        LanguageCode = "nb"
                    }
                ];

                dialog.SearchTags =
                [
                    new V1ServiceOwnerDialogsCommandsCreate_Tag
                    {
                        Value = "SBv2.9"
                    },
                    new V1ServiceOwnerDialogsCommandsCreate_Tag
                    {
                        Value = "trollsikringstjenesten"
                    },
                    new V1ServiceOwnerDialogsCommandsCreate_Tag
                    {
                        Value = "trolljeger"
                    }
                ];

                dialog.GuiActions = [new V1ServiceOwnerDialogsCommandsCreate_GuiAction(), new V1ServiceOwnerDialogsCommandsCreate_GuiAction()];

                // Amund: Føles feil ut på et fundamentalt nivå
                var enumerator = dialog.GuiActions.GetEnumerator();
                enumerator.MoveNext();
                enumerator.Current.Prefill(GuiActionPrefill.ReadConfirmation);
                enumerator.MoveNext();
                enumerator.Current.Prefill(GuiActionPrefill.DeleteButton);
                enumerator.Current.Priority = DialogsEntitiesActions_DialogGuiActionPriority.Secondary;
                enumerator.Dispose();

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(prefillType), prefillType, null);
        }

    }
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

public enum DialogPrefill
{
    Empty,
    Skattemeldig
}
