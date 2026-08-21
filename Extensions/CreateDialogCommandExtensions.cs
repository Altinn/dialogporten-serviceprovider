using System.Text.Json;
using Altinn.ApiClients.Dialogporten.Features.V1;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Extensions;

public static class CreateDialogCommandExtensions
{

    public static V1ServiceOwnerDialogsCommandsCreate_Dialog Copy(this V1ServiceOwnerDialogsCommandsCreate_Dialog dialog)
    {
        var json = JsonSerializer.Serialize(dialog);
        var newCommand = JsonSerializer.Deserialize<V1ServiceOwnerDialogsCommandsCreate_Dialog>(json);

        return newCommand ?? new V1ServiceOwnerDialogsCommandsCreate_Dialog();

    }

}
