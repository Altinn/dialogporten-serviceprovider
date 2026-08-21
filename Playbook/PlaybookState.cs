using System.Text.Json.Nodes;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public class PlaybookState(Guid dialogId, int cursor, JsonArray patches)
{
    public Guid DialogId { get; set; } = dialogId;
    public JsonArray Patches { get; set; } = patches;
    public int Cursor { get; set; } = cursor;
}
