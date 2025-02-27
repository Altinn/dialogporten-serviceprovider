using Altinn.ApiClients.Dialogporten.Features.V1;
using Digdir.BDB.Dialogporten.ServiceProvider.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Controllers;

[ApiController]
[Route("guiaction/write")]
[Authorize(AuthenticationSchemes = "DialogToken")]
[EnableCors("AllowedOriginsPolicy")]
public class WriteGuiActionController : Controller
{
    private readonly IServiceownerApi _dialogporten;
    private readonly IBackgroundTaskQueue _taskQueue;

    public WriteGuiActionController(
        IServiceownerApi dialogporten,
        IBackgroundTaskQueue taskQueue)
    {
        _dialogporten = dialogporten;
        _taskQueue = taskQueue;
    }

    [HttpPost]
    public async Task<IActionResult> Post(
        [FromQuery] string xacmlaction = "write",
        [FromQuery] bool queueInBackground = false,
        [FromQuery] bool addActivity = false,
        [FromQuery] bool addTransmission = false,
        [FromQuery] bool addAttachment = false,
        [FromQuery] bool setDialogGuiActionsToDeleteOnly = false,
        [FromQuery] DialogsEntities_DialogStatus? setStatusTo = null)
    {
        if (!IsAuthorized(xacmlaction))
        {
            return Forbid();
        }

        var operations = new List<JsonPatchOperations_Operation>();

        if (addActivity)
        {
            operations.Add(GetAddActivityOp());
        }

        if (addTransmission)
        {
            operations.Add(GetAddTransmissionOp());
        }

        if (addAttachment)
        {
            operations.Add(GetAddAttachmentOp());
        }

        if (setStatusTo.HasValue)
        {
            operations.Add(GetReplaceStatusOp(setStatusTo.Value));
        }

        if (setDialogGuiActionsToDeleteOnly)
        {
            operations.Add(GetReplaceGuiActionsOp());
        }

        return await PerformMaybeBackgroundOperation(queueInBackground, () =>
            _dialogporten.V1ServiceOwnerDialogsPatchDialog(GetDialogId(), operations, null, CancellationToken.None));

    }

    [HttpDelete]
    public async Task<IActionResult> Delete(
        [FromQuery] string xacmlaction = "write",
        [FromQuery] bool queueInBackground = false)
    {

        if (!IsAuthorized(xacmlaction))
        {
            return Forbid();
        }

        return await PerformMaybeBackgroundOperation(queueInBackground, () =>
            _dialogporten.V1ServiceOwnerDialogsDeleteDialog(GetDialogId(), null, CancellationToken.None));
    }

    private JsonPatchOperations_Operation GetReplaceGuiActionsOp()
    {
        return new JsonPatchOperations_Operation
        {
            Op = "replace",
            Path = "/guiActions",
            // Value = new List<UpdateDialogDialogGuiActionDto>
            Value = new List<V1ServiceOwnerDialogsCommandsUpdate_GuiAction>
            {
                new()
                {
                    Action = "write",
                    IsDeleteDialogAction = true,
                    HttpMethod = Http_HttpVerb.DELETE,
                    Title = new List<V1CommonLocalizations_Localization>
                    {
                        new()
                        {
                            LanguageCode = "en",
                            Value = "Delete dialog"
                        }
                    },
                    Url = GetActionUrl(typeof(WriteGuiActionController), nameof(Delete))
                }
            }
        };
    }

    private static JsonPatchOperations_Operation GetReplaceStatusOp(DialogsEntities_DialogStatus setStatusTo)
    {
        return new JsonPatchOperations_Operation
        {
            Op = "replace",
            Path = "/status",
            Value = setStatusTo
        };
    }

    private JsonPatchOperations_Operation GetAddAttachmentOp()
    {
        return new JsonPatchOperations_Operation
        {
            Op = "add",
            Path = "/attachments/-",
            // Value = new UpdateDialogDialogAttachmentDto
            Value = new V1ServiceOwnerDialogsCommandsUpdate_Attachment
            {
                DisplayName = new List<V1CommonLocalizations_Localization>
                {
                    new()
                    {
                        LanguageCode = "en",
                        Value = "Attachment added by dialogporten-serviceprovider"
                    }
                },
                Urls = new List<V1ServiceOwnerDialogsCommandsUpdate_AttachmentUrl>
                {
                    new()
                    {
                        ConsumerType = Attachments_AttachmentUrlConsumerType.Gui,
                        MediaType = "application/pdf",
                        Url = GetActionUrl(typeof(AttachmentController), nameof(AttachmentController.Get), new
                        {
                            fileName = "document.pdf"
                        })
                    }
                }
            }
        };
    }

    private JsonPatchOperations_Operation GetAddTransmissionOp()
    {
        return new JsonPatchOperations_Operation
        {
            Op = "add",
            Path = "/transmissions/-",
            Value = new V1ServiceOwnerDialogsCommandsUpdate_Transmission
            {
                Type = DialogsEntitiesTransmissions_DialogTransmissionType.Information,
                Sender = new V1ServiceOwnerCommonActors_Actor
                {
                    ActorType = Actors_ActorType.PartyRepresentative,
                    ActorId = GetActorId()
                },
                Content = new V1ServiceOwnerDialogsCommandsUpdate_TransmissionContent
                {
                    Title = new V1CommonContent_ContentValue
                    {
                        MediaType = "text/plain",
                        Value = new List<V1CommonLocalizations_Localization>
                        {
                            new()
                            {
                                LanguageCode = "en",
                                Value = "Transmission title added by dialogporten-serviceprovider"
                            }
                        }
                    },
                    Summary = new V1CommonContent_ContentValue
                    {
                        MediaType = "text/plain",
                        Value = new List<V1CommonLocalizations_Localization>
                        {
                            new()
                            {
                                LanguageCode = "en",
                                Value = "Transmission summary added by dialogporten-serviceprovider"
                            }
                        }
                    },
                }
            }
        };
    }

    private JsonPatchOperations_Operation GetAddActivityOp()
    {
        return new JsonPatchOperations_Operation
        {
            Op = "add",
            Path = "/activities/-",
            Value = new V1ServiceOwnerDialogsCommandsUpdate_Activity
            {
                Type = DialogsEntitiesActivities_DialogActivityType.Information,
                PerformedBy = new V1ServiceOwnerCommonActors_Actor
                {
                    ActorType = Actors_ActorType.PartyRepresentative,
                    ActorId = GetActorId()
                },
                Description = new List<V1CommonLocalizations_Localization>
                {
                    new()
                    {
                        LanguageCode = "en",
                        Value = "Activity added by dialogporten-serviceprovider"
                    }
                }
            }
        };
    }

    private bool IsAuthorized(string xacmlaction)
    {
        return User.Claims.Any(c => c.Type == "a" && c.Value.Split(';').Any(x => x == xacmlaction));
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

    private async Task<IActionResult> PerformMaybeBackgroundOperation(bool isDelayed, Func<Task> operation)
    {
        if (isDelayed)
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

    private Uri GetActionUrl(Type controllerType, string actionName, object? parameters = null)
    {
        var actionUrl = Url.Action(
            action: actionName,
            controller: controllerType.Name.Replace("Controller", ""),
            values: parameters,
            protocol: Request.Scheme,
            host: Request.Host.ToString()
        );

        return new Uri(actionUrl!);
    }
}
